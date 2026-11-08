using System.ComponentModel.DataAnnotations;
using Dapper;
using Hesap.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hesap.Features;

// =============================================================================================== Bakiye formülleri
// Tek yerde tanımlı; listeler, ekstre, raporlar ve pano aynı formülü kullanır.
internal static class Sql
{
    /// <summary>Cari bakiyesi (+ cari bize borçlu): açılış + satış − alış − tahsilat + ödeme (iptaller hariç). Takma ad: c.</summary>
    public const string CustomerBalance = @"
        (c.ACILIS_BAKIYESI
         + COALESCE((SELECT SUM(CASE f.TUR WHEN 'SA' THEN f.GENEL_TOPLAM ELSE -f.GENEL_TOPLAM END)
                     FROM t_faturalar f WHERE f.FIRMA_ID = c.FIRMA_ID AND f.CARI_ID = c.CARI_ID AND f.IPTAL_MI = 0), 0)
         + COALESCE((SELECT SUM(CASE h.TUR WHEN 'OD' THEN h.TUTAR ELSE -h.TUTAR END)
                     FROM t_hareketler h WHERE h.FIRMA_ID = c.FIRMA_ID AND h.CARI_ID = c.CARI_ID AND h.IPTAL_MI = 0 AND h.TUR IN ('TA', 'OD')), 0))";

    /// <summary>Kasa/banka bakiyesi: açılış + girişler − çıkışlar; virman kaynaktan düşer, hedefe eklenir. Takma ad: a.</summary>
    public const string AccountBalance = @"
        (a.ACILIS_BAKIYESI
         + COALESCE((SELECT SUM(CASE WHEN h.TUR IN ('TA', 'GE') THEN h.TUTAR ELSE -h.TUTAR END)
                     FROM t_hareketler h WHERE h.FIRMA_ID = a.FIRMA_ID AND h.HESAP_ID = a.HESAP_ID AND h.IPTAL_MI = 0), 0)
         + COALESCE((SELECT SUM(h.TUTAR)
                     FROM t_hareketler h WHERE h.FIRMA_ID = a.FIRMA_ID AND h.KARSI_HESAP_ID = a.HESAP_ID AND h.TUR = 'VI' AND h.IPTAL_MI = 0), 0))";

    /// <summary>Stok: açılış + alış − satış (iptaller hariç). Takma ad: u.</summary>
    public const string Stock = @"
        (u.ACILIS_STOK
         + COALESCE((SELECT SUM(CASE f.TUR WHEN 'AL' THEN s.MIKTAR ELSE -s.MIKTAR END)
                     FROM t_fatura_satirlari s JOIN t_faturalar f ON f.FATURA_ID = s.FATURA_ID
                     WHERE s.FIRMA_ID = u.FIRMA_ID AND s.URUN_ID = u.URUN_ID AND f.IPTAL_MI = 0), 0))";

    /// <summary>Faturaya bağlı (iptal edilmemiş) tahsilat/ödemelerin toplamı. Takma ad: f.</summary>
    public const string InvoicePaid = @"
        COALESCE((SELECT SUM(h.TUTAR) FROM t_hareketler h
                  WHERE h.FIRMA_ID = f.FIRMA_ID AND h.FATURA_ID = f.FATURA_ID AND h.IPTAL_MI = 0), 0)";

    /// <summary>LIKE araması için % ve _ kaçışlı arama metni.</summary>
    public static string? Like(string? q) =>
        string.IsNullOrWhiteSpace(q) ? null : "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    /// <summary>"C00001" gibi sıradaki kod (kod boş bırakılırsa).</summary>
    public static async Task<string> NextCodeAsync(System.Data.IDbConnection conn, string table, string prefix, long firmId, System.Data.IDbTransaction? tx = null)
    {
        var max = await conn.ExecuteScalarAsync<long?>(
            $"SELECT MAX(CAST(SUBSTRING(KOD, {prefix.Length + 1}) AS UNSIGNED)) FROM {table} WHERE FIRMA_ID = @firmId AND KOD REGEXP @pattern",
            new { firmId, pattern = $"^{prefix}[0-9]+$" }, tx);
        return $"{prefix}{(max ?? 0) + 1:D5}";
    }
}

// =============================================================================================== Cari hesaplar

public sealed class CustomerDto
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? TaxNumber { get; set; }
    public string? TaxOffice { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public int PaymentTermDays { get; set; }
    public decimal OpeningBalance { get; set; }
    public string? Notes { get; set; }
    public bool Active { get; set; }
    public decimal Balance { get; set; }
}

public sealed record CustomerSaveRequest(
    [Required, StringLength(2)] string Type,
    [StringLength(30)] string? Code,
    [Required, StringLength(200)] string Title,
    [StringLength(11)] string? TaxNumber,
    [StringLength(80)] string? TaxOffice,
    [StringLength(30)] string? Phone,
    [EmailAddress, StringLength(150)] string? Email,
    [StringLength(500)] string? Address,
    [StringLength(60)] string? City,
    [StringLength(80)] string? District,
    [Range(0, 365)] int PaymentTermDays,
    [Range(typeof(decimal), "-999999999999", "999999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal OpeningBalance,
    [StringLength(1000)] string? Notes,
    bool Active = true);

public sealed class StatementRow
{
    public DateTime Date { get; set; }
    public string Kind { get; set; } = "";
    public string KindLabel { get; set; } = "";
    public string? DocumentNo { get; set; }
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public long? RefId { get; set; }
}

public sealed record StatementDto(CustomerDto Customer, DateTime From, DateTime To, decimal OpeningBalance, IReadOnlyList<StatementRow> Rows, decimal ClosingBalance);

public sealed class CustomerService(Db db, Audit audit)
{
    private static readonly string Select = $@"
        SELECT c.CARI_ID AS Id, c.TIP AS Type, c.KOD AS Code, c.UNVAN AS Title, c.VERGI_NO AS TaxNumber, c.VERGI_DAIRESI AS TaxOffice,
               c.TELEFON AS Phone, c.EPOSTA AS Email, c.ADRES AS Address, c.IL AS City, c.ILCE AS District, c.VADE_GUN AS PaymentTermDays,
               c.ACILIS_BAKIYESI AS OpeningBalance, c.NOTLAR AS Notes, c.AKTIF_MI AS Active, {Sql.CustomerBalance} AS Balance
        FROM t_cariler c";

    public async Task<PagedResult<CustomerDto>> ListAsync(long firm, string? q, string? type, bool? active, PageQuery page)
    {
        var where = @"WHERE c.FIRMA_ID = @firm
              AND (@like IS NULL OR c.UNVAN LIKE @like OR c.KOD LIKE @like OR c.VERGI_NO LIKE @like OR c.TELEFON LIKE @like)
              AND (@type IS NULL OR c.TIP = @type OR (c.TIP = 'IK' AND @type IN ('MU', 'TE')))
              AND (@active IS NULL OR c.AKTIF_MI = @active)";
        var args = new { firm, like = Sql.Like(q), type = string.IsNullOrWhiteSpace(type) ? null : type, active, page.Offset, Size = page.SafeSize };

        await using var conn = await db.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM t_cariler c {where}", args);
        var items = (await conn.QueryAsync<CustomerDto>($"{Select} {where} ORDER BY c.UNVAN LIMIT @Size OFFSET @Offset", args)).ToList();
        return new PagedResult<CustomerDto>(items, total, page.SafePage, page.SafeSize);
    }

    public async Task<CustomerDto> GetAsync(long firm, long id)
    {
        await using var conn = await db.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<CustomerDto>($"{Select} WHERE c.FIRMA_ID = @firm AND c.CARI_ID = @id", new { firm, id })
               ?? throw AppException.NotFound("Cari");
    }

    private static void Validate(CustomerSaveRequest r)
    {
        if (r.Type is not ("MU" or "TE" or "IK"))
            throw new AppException("Cari türünü seçin: müşteri, tedarikçi veya her ikisi.");
        if (!TrIds.IsValidTaxOrNationalId(CompanyService.Clean(r.TaxNumber)))
            throw new AppException("Geçerli bir vergi no (10 hane) veya T.C. kimlik no (11 hane) girin.");
    }

    public async Task<long> SaveAsync(CurrentUser me, long? id, CustomerSaveRequest r)
    {
        Validate(r);
        await using var conn = await db.OpenAsync();

        var code = CompanyService.Clean(r.Code) ?? await Sql.NextCodeAsync(conn, "t_cariler", "C", me.Firm);
        var args = new
        {
            me.Firm, id, r.Type, code, Title = r.Title.Trim(), TaxNumber = CompanyService.Clean(r.TaxNumber), TaxOffice = CompanyService.Clean(r.TaxOffice),
            Phone = CompanyService.Clean(r.Phone), Email = CompanyService.Clean(r.Email)?.ToLowerInvariant(), Address = CompanyService.Clean(r.Address),
            City = CompanyService.Clean(r.City), District = CompanyService.Clean(r.District), r.PaymentTermDays,
            OpeningBalance = Money.R2(r.OpeningBalance), Notes = CompanyService.Clean(r.Notes), r.Active
        };

        if (id == null)
        {
            var newId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO t_cariler (FIRMA_ID, TIP, KOD, UNVAN, VERGI_NO, VERGI_DAIRESI, TELEFON, EPOSTA, ADRES, IL, ILCE, VADE_GUN, ACILIS_BAKIYESI, NOTLAR, AKTIF_MI)
                VALUES (@Firm, @Type, @code, @Title, @TaxNumber, @TaxOffice, @Phone, @Email, @Address, @City, @District, @PaymentTermDays, @OpeningBalance, @Notes, @Active);
                SELECT LAST_INSERT_ID();", args);
            await audit.LogAsync(me, "CARI_EKLE", "Cari", newId, args.Title);
            return newId;
        }

        var n = await conn.ExecuteAsync(@"
            UPDATE t_cariler SET TIP = @Type, KOD = @code, UNVAN = @Title, VERGI_NO = @TaxNumber, VERGI_DAIRESI = @TaxOffice, TELEFON = @Phone,
                   EPOSTA = @Email, ADRES = @Address, IL = @City, ILCE = @District, VADE_GUN = @PaymentTermDays,
                   ACILIS_BAKIYESI = @OpeningBalance, NOTLAR = @Notes, AKTIF_MI = @Active
            WHERE FIRMA_ID = @Firm AND CARI_ID = @id", args);
        if (n == 0)
            throw AppException.NotFound("Cari");

        await audit.LogAsync(me, "CARI_GUNCELLE", "Cari", id, args.Title);
        return id.Value;
    }

    /// <summary>Cari ekstresi: dönem öncesi devir + dönem içi fatura ve tahsilat/ödemeler, yürüyen bakiyeyle.</summary>
    public async Task<StatementDto> StatementAsync(long firm, long id, DateTime? from, DateTime? to)
    {
        var customer = await GetAsync(firm, id);
        var start = (from ?? new DateTime(DateTime.Today.Year, 1, 1)).Date;
        var end = (to ?? DateTime.Today).Date;
        if (end < start)
            throw new AppException("Bitiş tarihi başlangıçtan önce olamaz.");

        await using var conn = await db.OpenAsync();

        // Borç = cari bize borçlandı (satış faturası, cariye ödeme); alacak = cari alacaklandı (alış faturası, tahsilat).
        const string movements = @"
            SELECT f.TARIH AS Date, f.TUR AS Kind, f.BELGE_NO AS DocumentNo, f.ACIKLAMA AS Description,
                   CASE f.TUR WHEN 'SA' THEN f.GENEL_TOPLAM ELSE 0 END AS Debit,
                   CASE f.TUR WHEN 'AL' THEN f.GENEL_TOPLAM ELSE 0 END AS Credit, f.FATURA_ID AS RefId, f.KAYIT_TARIHI AS Created
            FROM t_faturalar f WHERE f.FIRMA_ID = @firm AND f.CARI_ID = @id AND f.IPTAL_MI = 0 {0}
            UNION ALL
            SELECT h.TARIH, h.TUR, h.BELGE_NO, h.ACIKLAMA,
                   CASE h.TUR WHEN 'OD' THEN h.TUTAR ELSE 0 END,
                   CASE h.TUR WHEN 'TA' THEN h.TUTAR ELSE 0 END, h.HAREKET_ID, h.KAYIT_TARIHI
            FROM t_hareketler h WHERE h.FIRMA_ID = @firm AND h.CARI_ID = @id AND h.IPTAL_MI = 0 AND h.TUR IN ('TA', 'OD') {1}";

        var before = await conn.ExecuteScalarAsync<decimal?>(
            $"SELECT SUM(Debit - Credit) FROM ({string.Format(movements, "AND f.TARIH < @start", "AND h.TARIH < @start")}) x",
            new { firm, id, start }) ?? 0m;

        var rows = (await conn.QueryAsync<StatementRow>(
            $"SELECT Date, Kind, DocumentNo, Description, Debit, Credit, RefId FROM ({string.Format(movements, "AND f.TARIH BETWEEN @start AND @end", "AND h.TARIH BETWEEN @start AND @end")}) x ORDER BY Date, Created",
            new { firm, id, start, end })).ToList();

        var opening = customer.OpeningBalance + before;
        var running = opening;
        foreach (var row in rows)
        {
            running += row.Debit - row.Credit;
            row.Balance = running;
            row.KindLabel = row.Kind switch { "SA" => "Satış faturası", "AL" => "Alış faturası", "TA" => "Tahsilat", "OD" => "Ödeme", _ => row.Kind };
        }

        return new StatementDto(customer, start, end, opening, rows, running);
    }
}

[Route("api/customers")]
public sealed class CustomersController(CustomerService svc) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> List(string? q, string? type, bool? active, [FromQuery] PageQuery page) =>
        OkData(await svc.ListAsync(Me.Firm, q, type, active, page));

    [HttpGet("{id:long}"), Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> Get(long id) => OkData(await svc.GetAsync(Me.Firm, id));

    [HttpPost, Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Create(CustomerSaveRequest r) => OkData(new { id = await svc.SaveAsync(Me, null, r) }, "Cari kaydedildi.");

    [HttpPut("{id:long}"), Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Update(long id, CustomerSaveRequest r)
    {
        await svc.SaveAsync(Me, id, r);
        return OkMessage("Cari güncellendi.");
    }

    [HttpGet("{id:long}/statement"), Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> Statement(long id, DateTime? from, DateTime? to) => OkData(await svc.StatementAsync(Me.Firm, id, from, to));
}

// =============================================================================================== Ürün / hizmet

public sealed class ProductDto
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal VatRate { get; set; }
    public decimal SalePrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public bool TrackStock { get; set; }
    public decimal OpeningStock { get; set; }
    public decimal? CriticalStock { get; set; }
    public bool Active { get; set; }
    public decimal? Stock { get; set; }
    public bool LowStock => TrackStock && CriticalStock.HasValue && Stock <= CriticalStock;
}

public sealed record ProductSaveRequest(
    [Required, StringLength(2)] string Type,
    [StringLength(40)] string? Code,
    [Required, StringLength(200)] string Name,
    [Required, StringLength(20)] string Unit,
    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal VatRate,
    [Range(typeof(decimal), "0", "999999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal SalePrice,
    [Range(typeof(decimal), "0", "999999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal PurchasePrice,
    bool TrackStock,
    [Range(typeof(decimal), "-999999999", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal OpeningStock,
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal? CriticalStock,
    bool Active = true);

public sealed class ProductService(Db db, Audit audit)
{
    /// <summary>Türkiye'de geçerli KDV oranları.</summary>
    public static readonly decimal[] VatRates = [0, 1, 10, 20];

    private static readonly string Select = $@"
        SELECT u.URUN_ID AS Id, u.TUR AS Type, u.KOD AS Code, u.AD AS Name, u.BIRIM AS Unit, u.KDV_ORANI AS VatRate,
               u.SATIS_FIYATI AS SalePrice, u.ALIS_FIYATI AS PurchasePrice, u.STOK_TAKIP AS TrackStock, u.ACILIS_STOK AS OpeningStock,
               u.KRITIK_STOK AS CriticalStock, u.AKTIF_MI AS Active, CASE WHEN u.STOK_TAKIP = 1 THEN {Sql.Stock} END AS Stock
        FROM t_urunler u";

    public async Task<PagedResult<ProductDto>> ListAsync(long firm, string? q, bool? active, PageQuery page)
    {
        const string where = @"WHERE u.FIRMA_ID = @firm AND (@like IS NULL OR u.AD LIKE @like OR u.KOD LIKE @like) AND (@active IS NULL OR u.AKTIF_MI = @active)";
        var args = new { firm, like = Sql.Like(q), active, page.Offset, Size = page.SafeSize };

        await using var conn = await db.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM t_urunler u {where}", args);
        var items = (await conn.QueryAsync<ProductDto>($"{Select} {where} ORDER BY u.AD LIMIT @Size OFFSET @Offset", args)).ToList();
        return new PagedResult<ProductDto>(items, total, page.SafePage, page.SafeSize);
    }

    public async Task<long> SaveAsync(CurrentUser me, long? id, ProductSaveRequest r)
    {
        if (r.Type is not ("UR" or "HZ"))
            throw new AppException("Türü seçin: ürün veya hizmet.");
        if (!VatRates.Contains(r.VatRate))
            throw new AppException("KDV oranı %0, %1, %10 veya %20 olmalıdır.");
        if (r.Type == "HZ" && r.TrackStock)
            throw new AppException("Hizmetlerde stok takibi yapılmaz.");

        await using var conn = await db.OpenAsync();
        var code = CompanyService.Clean(r.Code) ?? await Sql.NextCodeAsync(conn, "t_urunler", "U", me.Firm);
        var args = new
        {
            me.Firm, id, r.Type, code, Name = r.Name.Trim(), Unit = r.Unit.Trim(), r.VatRate, SalePrice = Math.Round(r.SalePrice, 4),
            PurchasePrice = Math.Round(r.PurchasePrice, 4), r.TrackStock, OpeningStock = Math.Round(r.OpeningStock, 3), r.CriticalStock, r.Active
        };

        if (id == null)
        {
            var newId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO t_urunler (FIRMA_ID, TUR, KOD, AD, BIRIM, KDV_ORANI, SATIS_FIYATI, ALIS_FIYATI, STOK_TAKIP, ACILIS_STOK, KRITIK_STOK, AKTIF_MI)
                VALUES (@Firm, @Type, @code, @Name, @Unit, @VatRate, @SalePrice, @PurchasePrice, @TrackStock, @OpeningStock, @CriticalStock, @Active);
                SELECT LAST_INSERT_ID();", args);
            await audit.LogAsync(me, "URUN_EKLE", "Urun", newId, args.Name);
            return newId;
        }

        var n = await conn.ExecuteAsync(@"
            UPDATE t_urunler SET TUR = @Type, KOD = @code, AD = @Name, BIRIM = @Unit, KDV_ORANI = @VatRate, SATIS_FIYATI = @SalePrice,
                   ALIS_FIYATI = @PurchasePrice, STOK_TAKIP = @TrackStock, ACILIS_STOK = @OpeningStock, KRITIK_STOK = @CriticalStock, AKTIF_MI = @Active
            WHERE FIRMA_ID = @Firm AND URUN_ID = @id", args);
        if (n == 0)
            throw AppException.NotFound("Ürün");

        await audit.LogAsync(me, "URUN_GUNCELLE", "Urun", id, args.Name);
        return id.Value;
    }
}

[Route("api/products")]
public sealed class ProductsController(ProductService svc) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> List(string? q, bool? active, [FromQuery] PageQuery page) => OkData(await svc.ListAsync(Me.Firm, q, active, page));

    [HttpPost, Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Create(ProductSaveRequest r) => OkData(new { id = await svc.SaveAsync(Me, null, r) }, "Ürün kaydedildi.");

    [HttpPut("{id:long}"), Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Update(long id, ProductSaveRequest r)
    {
        await svc.SaveAsync(Me, id, r);
        return OkMessage("Ürün güncellendi.");
    }
}

// =============================================================================================== Kasa / banka hesapları ve kategoriler

public sealed class AccountDto
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string? BankName { get; set; }
    public string? Iban { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool Active { get; set; }
    public decimal Balance { get; set; }
}

public sealed record AccountSaveRequest(
    [Required, StringLength(2)] string Type,
    [Required, StringLength(120)] string Name,
    [StringLength(120)] string? BankName,
    [StringLength(40)] string? Iban,
    [Range(typeof(decimal), "-999999999999", "999999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal OpeningBalance,
    bool Active = true);

public sealed class CategoryDto
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; }
}

public sealed record CategorySaveRequest([Required, StringLength(2)] string Type, [Required, StringLength(80)] string Name, bool Active = true);

public sealed class AccountService(Db db, Audit audit)
{
    private static readonly string Select = $@"
        SELECT a.HESAP_ID AS Id, a.TUR AS Type, a.AD AS Name, a.BANKA_ADI AS BankName, a.IBAN AS Iban,
               a.ACILIS_BAKIYESI AS OpeningBalance, a.AKTIF_MI AS Active, {Sql.AccountBalance} AS Balance
        FROM t_hesaplar a";

    public async Task<IReadOnlyList<AccountDto>> ListAsync(long firm)
    {
        await using var conn = await db.OpenAsync();
        return (await conn.QueryAsync<AccountDto>($"{Select} WHERE a.FIRMA_ID = @firm ORDER BY a.AKTIF_MI DESC, a.TUR DESC, a.AD", new { firm })).ToList();
    }

    public async Task<AccountDto> GetAsync(long firm, long id)
    {
        await using var conn = await db.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<AccountDto>($"{Select} WHERE a.FIRMA_ID = @firm AND a.HESAP_ID = @id", new { firm, id })
               ?? throw AppException.NotFound("Kasa/banka hesabı");
    }

    public async Task<long> SaveAsync(CurrentUser me, long? id, AccountSaveRequest r)
    {
        if (r.Type is not ("KA" or "BA"))
            throw new AppException("Hesap türünü seçin: kasa veya banka.");

        var iban = TrIds.NormalizeIban(r.Iban);
        if (iban != null && !TrIds.IsValidTrIban(iban))
            throw new AppException("Geçerli bir TR IBAN girin (TR ile başlayan 26 karakter).");

        await using var conn = await db.OpenAsync();
        var args = new { me.Firm, id, r.Type, Name = r.Name.Trim(), BankName = CompanyService.Clean(r.BankName), iban, OpeningBalance = Money.R2(r.OpeningBalance), r.Active };

        if (id == null)
        {
            var newId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO t_hesaplar (FIRMA_ID, TUR, AD, BANKA_ADI, IBAN, ACILIS_BAKIYESI, AKTIF_MI)
                VALUES (@Firm, @Type, @Name, @BankName, @iban, @OpeningBalance, @Active);
                SELECT LAST_INSERT_ID();", args);
            await audit.LogAsync(me, "HESAP_EKLE", "Hesap", newId, args.Name);
            return newId;
        }

        var n = await conn.ExecuteAsync(@"
            UPDATE t_hesaplar SET TUR = @Type, AD = @Name, BANKA_ADI = @BankName, IBAN = @iban, ACILIS_BAKIYESI = @OpeningBalance, AKTIF_MI = @Active
            WHERE FIRMA_ID = @Firm AND HESAP_ID = @id", args);
        if (n == 0)
            throw AppException.NotFound("Kasa/banka hesabı");

        await audit.LogAsync(me, "HESAP_GUNCELLE", "Hesap", id, args.Name);
        return id.Value;
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(long firm)
    {
        await using var conn = await db.OpenAsync();
        return (await conn.QueryAsync<CategoryDto>(
            "SELECT KATEGORI_ID AS Id, TUR AS Type, AD AS Name, AKTIF_MI AS Active FROM t_kategoriler WHERE FIRMA_ID = @firm ORDER BY TUR DESC, AD",
            new { firm })).ToList();
    }

    public async Task<long> SaveCategoryAsync(CurrentUser me, long? id, CategorySaveRequest r)
    {
        if (r.Type is not ("GI" or "GE"))
            throw new AppException("Kategori türünü seçin: gider veya gelir.");

        await using var conn = await db.OpenAsync();
        var args = new { me.Firm, id, r.Type, Name = r.Name.Trim(), r.Active };
        if (id == null)
            return await conn.ExecuteScalarAsync<long>(
                "INSERT INTO t_kategoriler (FIRMA_ID, TUR, AD, AKTIF_MI) VALUES (@Firm, @Type, @Name, @Active); SELECT LAST_INSERT_ID();", args);

        if (await conn.ExecuteAsync("UPDATE t_kategoriler SET TUR = @Type, AD = @Name, AKTIF_MI = @Active WHERE FIRMA_ID = @Firm AND KATEGORI_ID = @id", args) == 0)
            throw AppException.NotFound("Kategori");
        return id.Value;
    }
}

[Route("api/accounts")]
public sealed class AccountsController(AccountService svc) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> List() => OkData(await svc.ListAsync(Me.Firm));

    [HttpPost, Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Create(AccountSaveRequest r) => OkData(new { id = await svc.SaveAsync(Me, null, r) }, "Hesap kaydedildi.");

    [HttpPut("{id:long}"), Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Update(long id, AccountSaveRequest r)
    {
        await svc.SaveAsync(Me, id, r);
        return OkMessage("Hesap güncellendi.");
    }
}

[Route("api/categories")]
public sealed class CategoriesController(AccountService svc) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> List() => OkData(await svc.ListCategoriesAsync(Me.Firm));

    [HttpPost, Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Create(CategorySaveRequest r) => OkData(new { id = await svc.SaveCategoryAsync(Me, null, r) }, "Kategori kaydedildi.");

    [HttpPut("{id:long}"), Authorize(Policy = Policies.FirmWrite)]
    public async Task<IActionResult> Update(long id, CategorySaveRequest r)
    {
        await svc.SaveCategoryAsync(Me, id, r);
        return OkMessage("Kategori güncellendi.");
    }
}
