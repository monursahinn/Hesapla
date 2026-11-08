using System.ComponentModel.DataAnnotations;
using System.Data;
using Dapper;
using Hesapla.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace Hesapla.Features;

// =============================================================================================== Faturalar

public sealed record InvoiceLineRequest(
    long? ProductId,
    [StringLength(300)] string? Description,
    [Range(typeof(decimal), "0.001", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal Quantity,
    [StringLength(20)] string? Unit,
    [Range(typeof(decimal), "0", "999999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal UnitPrice,
    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal DiscountRate,
    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal VatRate);

public sealed record InvoiceSaveRequest(
    [Required, StringLength(2)] string Type,
    long CustomerId,
    DateTime Date,
    DateTime? DueDate,
    [StringLength(30)] string? DocumentNo,
    [StringLength(500)] string? Description,
    [Required, MinLength(1), MaxLength(200)] List<InvoiceLineRequest> Lines);

public sealed record CancelRequest([Required, StringLength(255)] string Reason);

public sealed class InvoiceListItem
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public long CustomerId { get; set; }
    public string CustomerTitle { get; set; } = "";
    public DateTime Date { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Remaining => Cancelled ? 0 : Total - Paid;
    public bool Cancelled { get; set; }
    public string Status => Cancelled ? "IP" : Remaining <= 0 ? "OD" : DueDate.Date < DateTime.Today ? "GE" : Paid > 0 ? "KO" : "BE";
    public string StatusLabel => Status switch { "IP" => "İptal", "OD" => "Ödendi", "GE" => "Vadesi geçti", "KO" => "Kısmi ödendi", _ => "Bekliyor" };
}

public sealed class InvoiceLineDto
{
    public int Order { get; set; }
    public long? ProductId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal VatRate { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
}

public sealed class InvoiceDto
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public long CustomerId { get; set; }
    public string CustomerTitle { get; set; } = "";
    public string? CustomerTaxNumber { get; set; }
    public string? CustomerTaxOffice { get; set; }
    public string? CustomerAddress { get; set; }
    public DateTime Date { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public string? Description { get; set; }
    public bool Cancelled { get; set; }
    public string? CancelReason { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = [];
    public List<MovementDto> Payments { get; set; } = [];
    public decimal Remaining => Cancelled ? 0 : Total - Paid;
    public IEnumerable<object> VatBreakdown => Lines.GroupBy(l => l.VatRate).OrderBy(g => g.Key)
        .Select(g => new { rate = g.Key, baseAmount = g.Sum(x => x.Amount), vat = g.Sum(x => x.VatAmount) });
}

public sealed record InvoiceListResult(PagedResult<InvoiceListItem> Page, decimal SumTotal, decimal SumRemaining);

public sealed class InvoiceService(Db db, Audit audit)
{
    private static readonly string ListSelect = $@"
        SELECT f.FATURA_ID AS Id, f.TUR AS Type, f.BELGE_NO AS DocumentNo, f.CARI_ID AS CustomerId, c.UNVAN AS CustomerTitle,
               f.TARIH AS Date, f.VADE_TARIHI AS DueDate, f.GENEL_TOPLAM AS Total, {Sql.InvoicePaid} AS Paid, f.IPTAL_MI AS Cancelled
        FROM t_faturalar f JOIN t_cariler c ON c.CARI_ID = f.CARI_ID";

    public async Task<InvoiceListResult> ListAsync(long firm, string? type, string? q, string? status, long? customerId, DateTime? from, DateTime? to, PageQuery page)
    {
        // Durum süzgeci: open = kalan > 0, paid = kalan 0, overdue = vadesi geçmiş ve kalan > 0, cancelled = iptal.
        var where = $@"WHERE f.FIRMA_ID = @firm
              AND (@type IS NULL OR f.TUR = @type)
              AND (@customerId IS NULL OR f.CARI_ID = @customerId)
              AND (@from IS NULL OR f.TARIH >= @from) AND (@to IS NULL OR f.TARIH <= @to)
              AND (@like IS NULL OR f.BELGE_NO LIKE @like OR c.UNVAN LIKE @like OR f.ACIKLAMA LIKE @like)
              AND (@status IS NULL
                   OR (@status = 'cancelled' AND f.IPTAL_MI = 1)
                   OR (@status = 'open' AND f.IPTAL_MI = 0 AND f.GENEL_TOPLAM > {Sql.InvoicePaid})
                   OR (@status = 'paid' AND f.IPTAL_MI = 0 AND f.GENEL_TOPLAM <= {Sql.InvoicePaid})
                   OR (@status = 'overdue' AND f.IPTAL_MI = 0 AND f.VADE_TARIHI < CURDATE() AND f.GENEL_TOPLAM > {Sql.InvoicePaid}))";

        var args = new
        {
            firm, type = string.IsNullOrWhiteSpace(type) ? null : type, customerId, from = from?.Date, to = to?.Date, like = Sql.Like(q),
            status = string.IsNullOrWhiteSpace(status) ? null : status, page.Offset, Size = page.SafeSize
        };

        await using var conn = await db.OpenAsync();
        var summary = await conn.QuerySingleAsync<(int Count, decimal? Total, decimal? Remaining)>($@"
            SELECT COUNT(*), SUM(CASE WHEN f.IPTAL_MI = 0 THEN f.GENEL_TOPLAM END),
                   SUM(CASE WHEN f.IPTAL_MI = 0 THEN f.GENEL_TOPLAM - {Sql.InvoicePaid} END)
            FROM t_faturalar f JOIN t_cariler c ON c.CARI_ID = f.CARI_ID {where}", args);
        var items = (await conn.QueryAsync<InvoiceListItem>($"{ListSelect} {where} ORDER BY f.TARIH DESC, f.FATURA_ID DESC LIMIT @Size OFFSET @Offset", args)).ToList();

        return new InvoiceListResult(new PagedResult<InvoiceListItem>(items, summary.Count, page.SafePage, page.SafeSize), summary.Total ?? 0, summary.Remaining ?? 0);
    }

    public async Task<InvoiceDto> GetAsync(long firm, long id)
    {
        await using var conn = await db.OpenAsync();
        var inv = await conn.QuerySingleOrDefaultAsync<InvoiceDto>($@"
            SELECT f.FATURA_ID AS Id, f.TUR AS Type, f.BELGE_NO AS DocumentNo, f.CARI_ID AS CustomerId, c.UNVAN AS CustomerTitle,
                   c.VERGI_NO AS CustomerTaxNumber, c.VERGI_DAIRESI AS CustomerTaxOffice,
                   CONCAT_WS(' ', c.ADRES, NULLIF(CONCAT_WS(' / ', c.ILCE, c.IL), '')) AS CustomerAddress,
                   f.TARIH AS Date, f.VADE_TARIHI AS DueDate, f.ARA_TOPLAM AS Subtotal, f.ISKONTO_TOPLAM AS DiscountTotal, f.KDV_TOPLAM AS VatTotal,
                   f.GENEL_TOPLAM AS Total, {Sql.InvoicePaid} AS Paid, f.ACIKLAMA AS Description, f.IPTAL_MI AS Cancelled, f.IPTAL_NEDENI AS CancelReason
            FROM t_faturalar f JOIN t_cariler c ON c.CARI_ID = f.CARI_ID
            WHERE f.FIRMA_ID = @firm AND f.FATURA_ID = @id", new { firm, id })
            ?? throw AppException.NotFound("Fatura");

        inv.Lines = (await conn.QueryAsync<InvoiceLineDto>(@"
            SELECT SIRA AS `Order`, URUN_ID AS ProductId, ACIKLAMA AS Description, MIKTAR AS Quantity, BIRIM AS Unit, BIRIM_FIYAT AS UnitPrice,
                   ISKONTO_ORANI AS DiscountRate, KDV_ORANI AS VatRate, TUTAR AS Amount, KDV_TUTARI AS VatAmount
            FROM t_fatura_satirlari WHERE FATURA_ID = @id AND FIRMA_ID = @firm ORDER BY SIRA", new { firm, id })).ToList();

        inv.Payments = (await conn.QueryAsync<MovementDto>($"{MovementService.Select} WHERE h.FIRMA_ID = @firm AND h.FATURA_ID = @id ORDER BY h.TARIH, h.HAREKET_ID",
            new { firm, id })).ToList();

        return inv;
    }

    private sealed record ComputedLine(int Order, long? ProductId, string Description, decimal Quantity, string Unit, decimal UnitPrice,
        decimal DiscountRate, decimal VatRate, decimal Gross, decimal Discount, decimal Amount, decimal VatAmount);

    /// <summary>
    /// Satır tutarları sunucuda hesaplanır: brüt = miktar × fiyat, iskonto = brüt × oran, matrah = brüt − iskonto,
    /// KDV = matrah × KDV oranı (her biri kuruşa yuvarlanır). İstemciden gelen toplamlar kullanılmaz.
    /// </summary>
    private static async Task<List<ComputedLine>> ComputeLinesAsync(IDbConnection conn, IDbTransaction tx, long firm, string type, List<InvoiceLineRequest> lines)
    {
        var productIds = lines.Where(l => l.ProductId.HasValue).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = productIds.Count == 0
            ? new Dictionary<long, (string Name, string Unit, bool Active)>()
            : (await conn.QueryAsync<(long Id, string Name, string Unit, bool Active)>(
                "SELECT URUN_ID, AD, BIRIM, AKTIF_MI FROM t_urunler WHERE FIRMA_ID = @firm AND URUN_ID IN @productIds", new { firm, productIds }, tx))
              .ToDictionary(p => p.Id, p => (p.Name, p.Unit, p.Active));

        var result = new List<ComputedLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var no = i + 1;

            (string Name, string Unit, bool Active) product = default;
            if (l.ProductId.HasValue && !products.TryGetValue(l.ProductId.Value, out product))
                throw new AppException($"{no}. satırdaki ürün bulunamadı.");
            if (l.ProductId.HasValue && !product.Active)
                throw new AppException($"{no}. satırdaki ürün pasif; önce ürünü aktifleştirin.");

            var description = CompanyService.Clean(l.Description) ?? product.Name;
            if (string.IsNullOrWhiteSpace(description))
                throw new AppException($"{no}. satır için ürün seçin veya açıklama yazın.");
            if (!ProductService.VatRates.Contains(l.VatRate))
                throw new AppException($"{no}. satırda KDV oranı %0, %1, %10 veya %20 olmalıdır.");

            var quantity = Math.Round(l.Quantity, 3);
            var unitPrice = Math.Round(l.UnitPrice, 4);
            if (quantity <= 0)
                throw new AppException($"{no}. satırda miktar 0'dan büyük olmalıdır.");

            var gross = Money.R2(quantity * unitPrice);
            var discount = Money.R2(gross * l.DiscountRate / 100m);
            var amount = gross - discount;
            var vat = Money.R2(amount * l.VatRate / 100m);

            result.Add(new ComputedLine(no, l.ProductId, description.Length > 300 ? description[..300] : description, quantity,
                CompanyService.Clean(l.Unit) ?? (l.ProductId.HasValue ? product.Unit ?? "Adet" : "Adet"), unitPrice, l.DiscountRate, l.VatRate, gross, discount, amount, vat));
        }

        if (result.Sum(x => x.Amount + x.VatAmount) > 999_999_999_999m)
            throw new AppException("Fatura toplamı çok büyük.");

        return result;
    }

    private static async Task<(string Title, string Type, int Term)> LoadCustomerAsync(IDbConnection conn, IDbTransaction tx, long firm, long customerId, string invoiceType)
    {
        var c = await conn.QuerySingleOrDefaultAsync<(string Title, string Type, int Term, bool Active)>(
            "SELECT UNVAN, TIP, VADE_GUN, AKTIF_MI FROM t_cariler WHERE FIRMA_ID = @firm AND CARI_ID = @customerId", new { firm, customerId }, tx);
        if (c.Title == null)
            throw AppException.NotFound("Cari");
        if (!c.Active)
            throw new AppException("Seçilen cari pasif; önce cariyi aktifleştirin.");
        if (invoiceType == "SA" && c.Type == "TE")
            throw new AppException("Satış faturası yalnızca müşteri carilere kesilir. Cari türünü \"Her ikisi\" yapabilirsiniz.");
        if (invoiceType == "AL" && c.Type == "MU")
            throw new AppException("Alış faturası yalnızca tedarikçi carilerden girilir. Cari türünü \"Her ikisi\" yapabilirsiniz.");
        return (c.Title, c.Type, c.Term);
    }

    private static void CheckDates(DateTime date, DateTime due)
    {
        if (date.Year < 2000 || date > DateTime.Today.AddYears(1))
            throw new AppException("Fatura tarihi geçerli değil.");
        if (due < date)
            throw new AppException("Vade tarihi fatura tarihinden önce olamaz.");
        if (due > date.AddYears(5))
            throw new AppException("Vade tarihi fatura tarihinden en fazla 5 yıl sonra olabilir.");
    }

    public async Task<long> CreateAsync(CurrentUser me, InvoiceSaveRequest r)
    {
        if (r.Type is not ("SA" or "AL"))
            throw new AppException("Fatura türünü seçin: satış veya alış.");

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        var customer = await LoadCustomerAsync(conn, tx, me.Firm, r.CustomerId, r.Type);
        var date = r.Date.Date;
        var due = (r.DueDate ?? date.AddDays(customer.Term)).Date;
        CheckDates(date, due);
        var lines = await ComputeLinesAsync(conn, tx, me.Firm, r.Type, r.Lines);

        // Satışta numara firma + yıl içinde atomik sayaçla verilir; alışta tedarikçinin numarası girilebilir.
        var documentNo = r.Type == "AL" ? CompanyService.Clean(r.DocumentNo) : null;
        if (documentNo == null)
        {
            var next = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO t_belge_sayac (FIRMA_ID, TUR, YIL, SON_NO) VALUES (@Firm, @Type, @Year, LAST_INSERT_ID(1))
                ON DUPLICATE KEY UPDATE SON_NO = LAST_INSERT_ID(SON_NO + 1);
                SELECT LAST_INSERT_ID();", new { me.Firm, r.Type, date.Year }, tx);
            documentNo = $"{(r.Type == "SA" ? "SAT" : "ALS")}{date.Year}{next:D9}";
        }

        long id;
        try
        {
            id = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO t_faturalar (FIRMA_ID, TUR, BELGE_NO, CARI_ID, TARIH, VADE_TARIHI, ARA_TOPLAM, ISKONTO_TOPLAM, KDV_TOPLAM, GENEL_TOPLAM, ACIKLAMA, OLUSTURAN_ID)
                VALUES (@Firm, @Type, @documentNo, @CustomerId, @date, @due, @Subtotal, @Discount, @Vat, @Total, @Description, @UserId);
                SELECT LAST_INSERT_ID();",
                new
                {
                    me.Firm, r.Type, documentNo, r.CustomerId, date, due, Subtotal = lines.Sum(x => x.Amount), Discount = lines.Sum(x => x.Discount),
                    Vat = lines.Sum(x => x.VatAmount), Total = lines.Sum(x => x.Amount + x.VatAmount), Description = CompanyService.Clean(r.Description), me.UserId
                }, tx);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw AppException.Conflict($"Bu cariden {documentNo} numaralı fatura zaten girilmiş.");
        }

        await InsertLinesAsync(conn, tx, me.Firm, id, lines);
        await tx.CommitAsync();

        await audit.LogAsync(me, r.Type == "SA" ? "SATIS_FATURASI" : "ALIS_FATURASI", "Fatura", id, $"{documentNo} {customer.Title} {Money.Format(lines.Sum(x => x.Amount + x.VatAmount))}");
        return id;
    }

    private static Task InsertLinesAsync(IDbConnection conn, IDbTransaction tx, long firm, long invoiceId, List<ComputedLine> lines) =>
        conn.ExecuteAsync(@"
            INSERT INTO t_fatura_satirlari (FATURA_ID, FIRMA_ID, SIRA, URUN_ID, ACIKLAMA, MIKTAR, BIRIM, BIRIM_FIYAT, ISKONTO_ORANI, KDV_ORANI, TUTAR, KDV_TUTARI)
            VALUES (@invoiceId, @firm, @Order, @ProductId, @Description, @Quantity, @Unit, @UnitPrice, @DiscountRate, @VatRate, @Amount, @VatAmount)",
            lines.Select(l => new { invoiceId, firm, l.Order, l.ProductId, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.DiscountRate, l.VatRate, l.Amount, l.VatAmount }), tx);

    /// <summary>Düzeltme yalnızca iptal edilmemiş ve bağlı tahsilat/ödemesi olmayan faturada yapılır; tür ve numara değişmez.</summary>
    public async Task UpdateAsync(CurrentUser me, long id, InvoiceSaveRequest r)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        var current = await conn.QuerySingleOrDefaultAsync<(string Type, string DocumentNo, bool Cancelled)>(
            "SELECT TUR, BELGE_NO, IPTAL_MI FROM t_faturalar WHERE FIRMA_ID = @Firm AND FATURA_ID = @id FOR UPDATE", new { me.Firm, id }, tx);
        if (current.Type == null)
            throw AppException.NotFound("Fatura");
        if (current.Cancelled)
            throw new AppException("İptal edilmiş fatura düzenlenemez.");
        if (r.Type != current.Type)
            throw new AppException("Fatura türü değiştirilemez.");
        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM t_hareketler WHERE FIRMA_ID = @Firm AND FATURA_ID = @id AND IPTAL_MI = 0", new { me.Firm, id }, tx) > 0)
            throw new AppException("Tahsilat/ödeme bağlanmış fatura düzenlenemez. Önce bağlı kayıtları iptal edin.");

        var customer = await LoadCustomerAsync(conn, tx, me.Firm, r.CustomerId, r.Type);
        var date = r.Date.Date;
        var due = (r.DueDate ?? date.AddDays(customer.Term)).Date;
        CheckDates(date, due);
        var lines = await ComputeLinesAsync(conn, tx, me.Firm, r.Type, r.Lines);

        var documentNo = r.Type == "AL" ? CompanyService.Clean(r.DocumentNo) ?? current.DocumentNo : current.DocumentNo;

        try
        {
            await conn.ExecuteAsync(@"
                UPDATE t_faturalar SET CARI_ID = @CustomerId, BELGE_NO = @documentNo, TARIH = @date, VADE_TARIHI = @due, ARA_TOPLAM = @Subtotal,
                       ISKONTO_TOPLAM = @Discount, KDV_TOPLAM = @Vat, GENEL_TOPLAM = @Total, ACIKLAMA = @Description
                WHERE FIRMA_ID = @Firm AND FATURA_ID = @id",
                new
                {
                    r.CustomerId, documentNo, date, due, Subtotal = lines.Sum(x => x.Amount), Discount = lines.Sum(x => x.Discount),
                    Vat = lines.Sum(x => x.VatAmount), Total = lines.Sum(x => x.Amount + x.VatAmount), Description = CompanyService.Clean(r.Description), me.Firm, id
                }, tx);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw AppException.Conflict($"Bu cariden {documentNo} numaralı fatura zaten girilmiş.");
        }

        await conn.ExecuteAsync("DELETE FROM t_fatura_satirlari WHERE FIRMA_ID = @Firm AND FATURA_ID = @id", new { me.Firm, id }, tx);
        await InsertLinesAsync(conn, tx, me.Firm, id, lines);
        await tx.CommitAsync();

        await audit.LogAsync(me, "FATURA_DUZENLE", "Fatura", id, $"{documentNo} {Money.Format(lines.Sum(x => x.Amount + x.VatAmount))}");
    }

    public async Task CancelAsync(CurrentUser me, long id, string reason)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        var current = await conn.QuerySingleOrDefaultAsync<(string DocumentNo, bool Cancelled)>(
            "SELECT BELGE_NO, IPTAL_MI FROM t_faturalar WHERE FIRMA_ID = @Firm AND FATURA_ID = @id FOR UPDATE", new { me.Firm, id }, tx);
        if (current.DocumentNo == null)
            throw AppException.NotFound("Fatura");
        if (current.Cancelled)
            throw new AppException("Fatura zaten iptal edilmiş.");
        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM t_hareketler WHERE FIRMA_ID = @Firm AND FATURA_ID = @id AND IPTAL_MI = 0", new { me.Firm, id }, tx) > 0)
            throw new AppException("Bu faturaya bağlı tahsilat/ödeme var. Önce onları iptal edin.");

        await conn.ExecuteAsync("UPDATE t_faturalar SET IPTAL_MI = 1, IPTAL_NEDENI = @reason WHERE FIRMA_ID = @Firm AND FATURA_ID = @id",
            new { reason = reason.Trim(), me.Firm, id }, tx);
        await tx.CommitAsync();

        await audit.LogAsync(me, "FATURA_IPTAL", "Fatura", id, $"{current.DocumentNo}: {reason.Trim()}");
    }
}

[Route("api/invoices")]
public sealed class InvoicesController(InvoiceService svc, InvoicePdf pdf) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> List(string? type, string? q, string? status, long? customerId, DateTime? from, DateTime? to, [FromQuery] PageQuery page) =>
        OkData(await svc.ListAsync(Me.Firm, type, q, status, customerId, from, to, page));

    [HttpGet("{id:long}"), Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> Get(long id) => OkData(await svc.GetAsync(Me.Firm, id));

    [HttpPost, Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Create(InvoiceSaveRequest r)
    {
        var id = await svc.CreateAsync(Me, r);
        return OkData(await svc.GetAsync(Me.Firm, id), "Fatura kaydedildi.");
    }

    [HttpPut("{id:long}"), Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Update(long id, InvoiceSaveRequest r)
    {
        await svc.UpdateAsync(Me, id, r);
        return OkData(await svc.GetAsync(Me.Firm, id), "Fatura güncellendi.");
    }

    [HttpPost("{id:long}/cancel"), Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Cancel(long id, CancelRequest r)
    {
        await svc.CancelAsync(Me, id, r.Reason);
        return OkMessage("Fatura iptal edildi.");
    }

    [HttpGet("{id:long}/pdf"), Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> Pdf(long id)
    {
        var (bytes, name) = await pdf.RenderAsync(Me.Firm, id);
        return File(bytes, "application/pdf", name);
    }
}

// =============================================================================================== Kasa / banka hareketleri

public sealed class MovementDto
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string TypeLabel => Type switch { "TA" => "Tahsilat", "OD" => "Ödeme", "GI" => "Gider", "GE" => "Gelir", "VI" => "Virman", _ => Type };
    public DateTime Date { get; set; }
    public long AccountId { get; set; }
    public string AccountName { get; set; } = "";
    public long? TargetAccountId { get; set; }
    public string? TargetAccountName { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerTitle { get; set; }
    public long? InvoiceId { get; set; }
    public string? InvoiceNo { get; set; }
    public long? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public decimal Amount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public string? DocumentNo { get; set; }
    public string? Description { get; set; }
    public bool Cancelled { get; set; }
    public string? CancelReason { get; set; }
}

public sealed record MovementSaveRequest(
    [Required, StringLength(2)] string Type,
    DateTime Date,
    long AccountId,
    long? TargetAccountId,
    long? CustomerId,
    long? InvoiceId,
    long? CategoryId,
    [Range(typeof(decimal), "0.01", "999999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal Amount,
    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal VatRate,
    [StringLength(40)] string? DocumentNo,
    [StringLength(500)] string? Description);

public sealed class MovementService(Db db, Audit audit)
{
    internal const string Select = @"
        SELECT h.HAREKET_ID AS Id, h.TUR AS Type, h.TARIH AS Date, h.HESAP_ID AS AccountId, a.AD AS AccountName,
               h.KARSI_HESAP_ID AS TargetAccountId, ka.AD AS TargetAccountName, h.CARI_ID AS CustomerId, c.UNVAN AS CustomerTitle,
               h.FATURA_ID AS InvoiceId, f.BELGE_NO AS InvoiceNo, h.KATEGORI_ID AS CategoryId, k.AD AS CategoryName,
               h.TUTAR AS Amount, h.KDV_ORANI AS VatRate, h.KDV_TUTARI AS VatAmount, h.BELGE_NO AS DocumentNo, h.ACIKLAMA AS Description,
               h.IPTAL_MI AS Cancelled, h.IPTAL_NEDENI AS CancelReason
        FROM t_hareketler h
        JOIN t_hesaplar a ON a.HESAP_ID = h.HESAP_ID
        LEFT JOIN t_hesaplar ka ON ka.HESAP_ID = h.KARSI_HESAP_ID
        LEFT JOIN t_cariler c ON c.CARI_ID = h.CARI_ID
        LEFT JOIN t_faturalar f ON f.FATURA_ID = h.FATURA_ID
        LEFT JOIN t_kategoriler k ON k.KATEGORI_ID = h.KATEGORI_ID";

    public async Task<PagedResult<MovementDto>> ListAsync(long firm, string? type, long? accountId, long? customerId, DateTime? from, DateTime? to, string? q, PageQuery page)
    {
        const string where = @"WHERE h.FIRMA_ID = @firm
              AND (@type IS NULL OR h.TUR = @type)
              AND (@accountId IS NULL OR h.HESAP_ID = @accountId OR h.KARSI_HESAP_ID = @accountId)
              AND (@customerId IS NULL OR h.CARI_ID = @customerId)
              AND (@from IS NULL OR h.TARIH >= @from) AND (@to IS NULL OR h.TARIH <= @to)
              AND (@like IS NULL OR h.ACIKLAMA LIKE @like OR h.BELGE_NO LIKE @like OR c.UNVAN LIKE @like OR k.AD LIKE @like)";
        var args = new
        {
            firm, type = string.IsNullOrWhiteSpace(type) ? null : type, accountId, customerId, from = from?.Date, to = to?.Date,
            like = Sql.Like(q), page.Offset, Size = page.SafeSize
        };

        await using var conn = await db.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($@"SELECT COUNT(*) FROM t_hareketler h
            LEFT JOIN t_cariler c ON c.CARI_ID = h.CARI_ID LEFT JOIN t_kategoriler k ON k.KATEGORI_ID = h.KATEGORI_ID {where}", args);
        var items = (await conn.QueryAsync<MovementDto>($"{Select} {where} ORDER BY h.TARIH DESC, h.HAREKET_ID DESC LIMIT @Size OFFSET @Offset", args)).ToList();
        return new PagedResult<MovementDto>(items, total, page.SafePage, page.SafeSize);
    }

    /// <summary>
    /// Kasa/banka hesaplarını kilitler (aynı hesaba eşzamanlı kayıtlar sırayla işlenir) ve işlem sonrası
    /// kasaların eksiye düşmediğini denetler. Banka hesapları (kredili mevduat) eksiye düşebilir.
    /// </summary>
    private static async Task CheckCashNotNegativeAsync(IDbConnection conn, IDbTransaction tx, long firm, IEnumerable<long> accountIds)
    {
        foreach (var id in accountIds.Distinct())
        {
            var acc = await conn.QuerySingleAsync<(string Type, string Name, decimal Balance)>(
                $"SELECT a.TUR, a.AD, {Sql.AccountBalance} FROM t_hesaplar a WHERE a.FIRMA_ID = @firm AND a.HESAP_ID = @id", new { firm, id }, tx);
            if (acc.Type == "KA" && acc.Balance < 0)
                throw new AppException($"{acc.Name} bakiyesi yetersiz (işlem sonrası {Money.Format(acc.Balance)} olurdu). Kasa eksiye düşemez.");
        }
    }

    private static async Task LockAccountsAsync(IDbConnection conn, IDbTransaction tx, long firm, params long?[] ids)
    {
        var list = ids.Where(x => x.HasValue).Select(x => x!.Value).Distinct().OrderBy(x => x).ToList();
        if (list.Count == 0)
            return;
        var found = (await conn.QueryAsync<(long Id, bool Active)>(
            "SELECT HESAP_ID, AKTIF_MI FROM t_hesaplar WHERE FIRMA_ID = @firm AND HESAP_ID IN @list ORDER BY HESAP_ID FOR UPDATE", new { firm, list }, tx)).ToList();
        if (found.Count != list.Count)
            throw AppException.NotFound("Kasa/banka hesabı");
        if (found.Any(x => !x.Active))
            throw new AppException("Seçilen kasa/banka hesabı pasif.");
    }

    public async Task<long> CreateAsync(CurrentUser me, MovementSaveRequest r)
    {
        if (r.Type is not ("TA" or "OD" or "GI" or "GE" or "VI"))
            throw new AppException("Hareket türünü seçin.");
        var date = r.Date.Date;
        if (date.Year < 2000 || date > DateTime.Today.AddDays(1))
            throw new AppException("Tarih geçerli değil (ileri tarihli kasa/banka hareketi girilemez).");

        var amount = Money.R2(r.Amount);
        long? customerId = null, invoiceId = null, categoryId = null, targetId = null;
        decimal vatRate = 0, vatAmount = 0;

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        switch (r.Type)
        {
            case "VI":
                if (r.TargetAccountId == null || r.TargetAccountId == r.AccountId)
                    throw new AppException("Virman için farklı bir karşı hesap seçin.");
                targetId = r.TargetAccountId;
                break;

            case "TA" or "OD":
                if (r.CustomerId == null)
                    throw new AppException(r.Type == "TA" ? "Tahsilat için cari seçin." : "Ödeme için cari seçin.");
                var c = await conn.QuerySingleOrDefaultAsync<(string Title, bool Active)>(
                    "SELECT UNVAN, AKTIF_MI FROM t_cariler WHERE FIRMA_ID = @Firm AND CARI_ID = @CustomerId", new { me.Firm, r.CustomerId }, tx);
                if (c.Title == null)
                    throw AppException.NotFound("Cari");
                customerId = r.CustomerId;

                if (r.InvoiceId.HasValue)
                {
                    // Fatura satırı kilitlenir: aynı faturaya eşzamanlı iki tahsilat kalan tutarı aşamaz.
                    var inv = await conn.QuerySingleOrDefaultAsync<(string Type, long CustomerId, bool Cancelled, decimal Total, string DocumentNo)>(
                        "SELECT TUR, CARI_ID, IPTAL_MI, GENEL_TOPLAM, BELGE_NO FROM t_faturalar WHERE FIRMA_ID = @Firm AND FATURA_ID = @InvoiceId FOR UPDATE",
                        new { me.Firm, r.InvoiceId }, tx);
                    if (inv.Type == null)
                        throw AppException.NotFound("Fatura");
                    if (inv.CustomerId != r.CustomerId)
                        throw new AppException("Seçilen fatura bu cariye ait değil.");
                    if (inv.Cancelled)
                        throw new AppException("İptal edilmiş faturaya tahsilat/ödeme bağlanamaz.");
                    if ((r.Type == "TA") != (inv.Type == "SA"))
                        throw new AppException(r.Type == "TA" ? "Tahsilat yalnızca satış faturasına bağlanır." : "Ödeme yalnızca alış faturasına bağlanır.");

                    var paid = await conn.ExecuteScalarAsync<decimal?>(
                        "SELECT SUM(TUTAR) FROM t_hareketler WHERE FIRMA_ID = @Firm AND FATURA_ID = @InvoiceId AND IPTAL_MI = 0", new { me.Firm, r.InvoiceId }, tx) ?? 0;
                    var remaining = inv.Total - paid;
                    if (amount > remaining)
                        throw new AppException($"{inv.DocumentNo} faturasının kalan tutarı {Money.Format(remaining)}; daha fazlası bağlanamaz.");
                    invoiceId = r.InvoiceId;
                }
                break;

            default: // GI, GE
                if (r.CategoryId == null)
                    throw new AppException("Kategori seçin.");
                var catType = await conn.ExecuteScalarAsync<string?>(
                    "SELECT TUR FROM t_kategoriler WHERE FIRMA_ID = @Firm AND KATEGORI_ID = @CategoryId AND AKTIF_MI = 1", new { me.Firm, r.CategoryId }, tx);
                if (catType == null)
                    throw AppException.NotFound("Kategori");
                if (catType != r.Type)
                    throw new AppException(r.Type == "GI" ? "Gider için gider kategorisi seçin." : "Gelir için gelir kategorisi seçin.");
                if (!ProductService.VatRates.Contains(r.VatRate))
                    throw new AppException("KDV oranı %0, %1, %10 veya %20 olmalıdır.");
                categoryId = r.CategoryId;
                vatRate = r.VatRate;
                // Tutar KDV dahildir: KDV = tutar × oran / (100 + oran).
                vatAmount = Money.R2(amount * vatRate / (100m + vatRate));
                break;
        }

        await LockAccountsAsync(conn, tx, me.Firm, r.AccountId, targetId);

        var id = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO t_hareketler (FIRMA_ID, TUR, TARIH, HESAP_ID, KARSI_HESAP_ID, CARI_ID, FATURA_ID, KATEGORI_ID, TUTAR, KDV_ORANI, KDV_TUTARI, BELGE_NO, ACIKLAMA, OLUSTURAN_ID)
            VALUES (@Firm, @Type, @date, @AccountId, @targetId, @customerId, @invoiceId, @categoryId, @amount, @vatRate, @vatAmount, @DocumentNo, @Description, @UserId);
            SELECT LAST_INSERT_ID();",
            new
            {
                me.Firm, r.Type, date, r.AccountId, targetId, customerId, invoiceId, categoryId, amount, vatRate, vatAmount,
                DocumentNo = CompanyService.Clean(r.DocumentNo), Description = CompanyService.Clean(r.Description), me.UserId
            }, tx);

        if (r.Type is "OD" or "GI" or "VI")
            await CheckCashNotNegativeAsync(conn, tx, me.Firm, [r.AccountId]);

        await tx.CommitAsync();
        await audit.LogAsync(me, $"HAREKET_{r.Type}", "Hareket", id, Money.Format(amount));
        return id;
    }

    /// <summary>İptal: kayıt silinmez, işaretlenir. Kasaya giriş iptal edilince kasa eksiye düşecekse reddedilir.</summary>
    public async Task CancelAsync(CurrentUser me, long id, string reason)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        var m = await conn.QuerySingleOrDefaultAsync<(string Type, long AccountId, long? TargetId, bool Cancelled, decimal Amount)>(
            "SELECT TUR, HESAP_ID, KARSI_HESAP_ID, IPTAL_MI, TUTAR FROM t_hareketler WHERE FIRMA_ID = @Firm AND HAREKET_ID = @id FOR UPDATE", new { me.Firm, id }, tx);
        if (m.Type == null)
            throw AppException.NotFound("Hareket");
        if (m.Cancelled)
            throw new AppException("Kayıt zaten iptal edilmiş.");

        await conn.QueryAsync("SELECT HESAP_ID FROM t_hesaplar WHERE FIRMA_ID = @Firm AND HESAP_ID IN @ids ORDER BY HESAP_ID FOR UPDATE",
            new { me.Firm, ids = new[] { m.AccountId, m.TargetId ?? m.AccountId } }, tx);

        await conn.ExecuteAsync("UPDATE t_hareketler SET IPTAL_MI = 1, IPTAL_NEDENI = @reason WHERE FIRMA_ID = @Firm AND HAREKET_ID = @id",
            new { reason = reason.Trim(), me.Firm, id }, tx);

        // Girişi geri alınan hesap: tahsilat/gelirde kendi hesabı, virmanda hedef hesap.
        var reduced = m.Type switch { "TA" or "GE" => m.AccountId, "VI" => m.TargetId, _ => null };
        if (reduced.HasValue)
            await CheckCashNotNegativeAsync(conn, tx, me.Firm, [reduced.Value]);

        await tx.CommitAsync();
        await audit.LogAsync(me, "HAREKET_IPTAL", "Hareket", id, $"{Money.Format(m.Amount)}: {reason.Trim()}");
    }
}

[Route("api/movements")]
public sealed class MovementsController(MovementService svc) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> List(string? type, long? accountId, long? customerId, DateTime? from, DateTime? to, string? q, [FromQuery] PageQuery page) =>
        OkData(await svc.ListAsync(Me.Firm, type, accountId, customerId, from, to, q, page));

    [HttpPost, Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Create(MovementSaveRequest r) => OkData(new { id = await svc.CreateAsync(Me, r) }, "Kayıt eklendi.");

    [HttpPost("{id:long}/cancel"), Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Cancel(long id, CancelRequest r)
    {
        await svc.CancelAsync(Me, id, r.Reason);
        return OkMessage("Kayıt iptal edildi.");
    }
}
