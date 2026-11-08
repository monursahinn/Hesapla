using Dapper;
using Hesap.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hesap.Features;

public sealed record MonthPoint(string Month, decimal Income, decimal Expense);

public sealed record DayPoint(DateTime Day, decimal In, decimal Out);

public sealed record DashboardDto(
    decimal CashTotal, decimal BankTotal, decimal Receivables, decimal Payables,
    decimal MonthSales, decimal MonthPurchases, decimal MonthExpenses, decimal MonthCollections,
    decimal OverdueReceivables, IReadOnlyList<InvoiceListItem> OverdueInvoices, IReadOnlyList<InvoiceListItem> UpcomingPayables,
    IReadOnlyList<MonthPoint> Last12Months, IReadOnlyList<DayPoint> CashFlow30, IReadOnlyList<ProductDto> LowStock,
    IReadOnlyList<AccountDto> Accounts);

public sealed record CategoryAmount(string Category, decimal Amount);

/// <summary>
/// Basit kâr/zarar (KDV hariç): satışlar − alışlar = brüt kâr; + diğer gelirler − giderler = net kâr.
/// Alışlar dönem gideri sayılır (stok maliyetlendirmesi yapılmaz); ön muhasebe için yönetim raporudur, resmi bilanço değildir.
/// </summary>
public sealed record ProfitLossDto(DateTime From, DateTime To, decimal Sales, decimal Purchases, decimal GrossProfit,
    decimal OtherIncome, IReadOnlyList<CategoryAmount> OtherIncomeByCategory, decimal Expenses, IReadOnlyList<CategoryAmount> ExpensesByCategory, decimal NetProfit);

public sealed record VatRateRow(decimal Rate, decimal BaseAmount, decimal Vat);

public sealed record VatDto(int Year, int Month, decimal OutputVat, IReadOnlyList<VatRateRow> Sales, decimal InputVat,
    IReadOnlyList<VatRateRow> Purchases, decimal ExpenseVat, decimal Payable, decimal CarryForward);

public sealed class AgingRow
{
    public long CustomerId { get; set; }
    public string CustomerTitle { get; set; } = "";
    public decimal NotDue { get; set; }
    public decimal D0To30 { get; set; }
    public decimal D31To60 { get; set; }
    public decimal D61To90 { get; set; }
    public decimal Over90 { get; set; }
    public decimal Total => NotDue + D0To30 + D31To60 + D61To90 + Over90;
    public decimal Balance { get; set; }
}

public sealed class ReportService(Db db, AccountService accounts)
{
    public async Task<DashboardDto> DashboardAsync(long firm)
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var accountList = await accounts.ListAsync(firm);

        await using var conn = await db.OpenAsync();
        var args = new { firm, monthStart, today };

        var balances = await conn.QuerySingleAsync<(decimal? Receivables, decimal? Payables)>($@"
            SELECT SUM(CASE WHEN b > 0 THEN b END), -SUM(CASE WHEN b < 0 THEN b END)
            FROM (SELECT {Sql.CustomerBalance} AS b FROM t_cariler c WHERE c.FIRMA_ID = @firm) x", args);

        var month = await conn.QuerySingleAsync<(decimal? Sales, decimal? Purchases, decimal? Expenses, decimal? Collections)>(@"
            SELECT (SELECT SUM(GENEL_TOPLAM) FROM t_faturalar WHERE FIRMA_ID = @firm AND TUR = 'SA' AND IPTAL_MI = 0 AND TARIH >= @monthStart),
                   (SELECT SUM(GENEL_TOPLAM) FROM t_faturalar WHERE FIRMA_ID = @firm AND TUR = 'AL' AND IPTAL_MI = 0 AND TARIH >= @monthStart),
                   (SELECT SUM(TUTAR) FROM t_hareketler WHERE FIRMA_ID = @firm AND TUR = 'GI' AND IPTAL_MI = 0 AND TARIH >= @monthStart),
                   (SELECT SUM(TUTAR) FROM t_hareketler WHERE FIRMA_ID = @firm AND TUR = 'TA' AND IPTAL_MI = 0 AND TARIH >= @monthStart)", args);

        var invoiceSelect = $@"
            SELECT f.FATURA_ID AS Id, f.TUR AS Type, f.BELGE_NO AS DocumentNo, f.CARI_ID AS CustomerId, c.UNVAN AS CustomerTitle,
                   f.TARIH AS Date, f.VADE_TARIHI AS DueDate, f.GENEL_TOPLAM AS Total, {Sql.InvoicePaid} AS Paid, f.IPTAL_MI AS Cancelled
            FROM t_faturalar f JOIN t_cariler c ON c.CARI_ID = f.CARI_ID
            WHERE f.FIRMA_ID = @firm AND f.IPTAL_MI = 0 AND f.GENEL_TOPLAM > {Sql.InvoicePaid}";

        var overdueSum = await conn.ExecuteScalarAsync<decimal?>($@"
            SELECT SUM(f.GENEL_TOPLAM - {Sql.InvoicePaid}) FROM t_faturalar f
            WHERE f.FIRMA_ID = @firm AND f.TUR = 'SA' AND f.IPTAL_MI = 0 AND f.VADE_TARIHI < @today AND f.GENEL_TOPLAM > {Sql.InvoicePaid}", args);
        var overdue = (await conn.QueryAsync<InvoiceListItem>($"{invoiceSelect} AND f.TUR = 'SA' AND f.VADE_TARIHI < @today ORDER BY f.VADE_TARIHI LIMIT 5", args)).ToList();
        var upcoming = (await conn.QueryAsync<InvoiceListItem>(
            $"{invoiceSelect} AND f.TUR = 'AL' AND f.VADE_TARIHI <= DATE_ADD(@today, INTERVAL 7 DAY) ORDER BY f.VADE_TARIHI LIMIT 5", args)).ToList();

        // Son 12 ay: gelir = satış faturaları + diğer gelirler; gider = alış faturaları + giderler (KDV dahil).
        var rows = (await conn.QueryAsync<(string Month, decimal Income, decimal Expense)>(@"
            SELECT DATE_FORMAT(TARIH, '%Y-%m'), SUM(GELIR), SUM(GIDER) FROM (
                SELECT TARIH, CASE TUR WHEN 'SA' THEN GENEL_TOPLAM ELSE 0 END AS GELIR, CASE TUR WHEN 'AL' THEN GENEL_TOPLAM ELSE 0 END AS GIDER
                FROM t_faturalar WHERE FIRMA_ID = @firm AND IPTAL_MI = 0 AND TARIH >= DATE_SUB(@monthStart, INTERVAL 11 MONTH)
                UNION ALL
                SELECT TARIH, CASE TUR WHEN 'GE' THEN TUTAR ELSE 0 END, CASE TUR WHEN 'GI' THEN TUTAR ELSE 0 END
                FROM t_hareketler WHERE FIRMA_ID = @firm AND IPTAL_MI = 0 AND TUR IN ('GE', 'GI') AND TARIH >= DATE_SUB(@monthStart, INTERVAL 11 MONTH)
            ) x GROUP BY DATE_FORMAT(TARIH, '%Y-%m')", args)).ToDictionary(x => x.Month);
        var months = Enumerable.Range(0, 12).Select(i => monthStart.AddMonths(i - 11).ToString("yyyy-MM"))
            .Select(m => rows.TryGetValue(m, out var v) ? new MonthPoint(m, v.Income, v.Expense) : new MonthPoint(m, 0, 0)).ToList();

        // Son 30 gün nakit akışı (kasa + banka; virman iç transfer olduğu için sayılmaz).
        var flows = (await conn.QueryAsync<(DateTime Day, decimal In, decimal Out)>(@"
            SELECT TARIH, SUM(CASE WHEN TUR IN ('TA', 'GE') THEN TUTAR ELSE 0 END), SUM(CASE WHEN TUR IN ('OD', 'GI') THEN TUTAR ELSE 0 END)
            FROM t_hareketler WHERE FIRMA_ID = @firm AND IPTAL_MI = 0 AND TUR <> 'VI' AND TARIH > DATE_SUB(@today, INTERVAL 30 DAY)
            GROUP BY TARIH", args)).ToDictionary(x => x.Day.Date);
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29))
            .Select(d => flows.TryGetValue(d, out var v) ? new DayPoint(d, v.In, v.Out) : new DayPoint(d, 0, 0)).ToList();

        var lowStock = (await conn.QueryAsync<ProductDto>($@"
            SELECT u.URUN_ID AS Id, u.TUR AS Type, u.KOD AS Code, u.AD AS Name, u.BIRIM AS Unit, u.KDV_ORANI AS VatRate, u.SATIS_FIYATI AS SalePrice,
                   u.ALIS_FIYATI AS PurchasePrice, u.STOK_TAKIP AS TrackStock, u.ACILIS_STOK AS OpeningStock, u.KRITIK_STOK AS CriticalStock,
                   u.AKTIF_MI AS Active, {Sql.Stock} AS Stock
            FROM t_urunler u WHERE u.FIRMA_ID = @firm AND u.AKTIF_MI = 1 AND u.STOK_TAKIP = 1 AND u.KRITIK_STOK IS NOT NULL
              AND {Sql.Stock} <= u.KRITIK_STOK ORDER BY u.AD LIMIT 5", args)).ToList();

        var active = accountList.Where(a => a.Active).ToList();
        return new DashboardDto(
            active.Where(a => a.Type == "KA").Sum(a => a.Balance), active.Where(a => a.Type == "BA").Sum(a => a.Balance),
            balances.Receivables ?? 0, balances.Payables ?? 0,
            month.Sales ?? 0, month.Purchases ?? 0, month.Expenses ?? 0, month.Collections ?? 0,
            overdueSum ?? 0, overdue, upcoming, months, days, lowStock, active);
    }

    private static (DateTime From, DateTime To) Range(DateTime? from, DateTime? to)
    {
        var start = (from ?? new DateTime(DateTime.Today.Year, 1, 1)).Date;
        var end = (to ?? DateTime.Today).Date;
        if (end < start)
            throw new AppException("Bitiş tarihi başlangıçtan önce olamaz.");
        if ((end - start).TotalDays > 3660)
            throw new AppException("Rapor aralığı en fazla 10 yıl olabilir.");
        return (start, end);
    }

    public async Task<ProfitLossDto> ProfitLossAsync(long firm, DateTime? from, DateTime? to)
    {
        var (start, end) = Range(from, to);
        var args = new { firm, start, end };

        await using var conn = await db.OpenAsync();
        var inv = await conn.QuerySingleAsync<(decimal? Sales, decimal? Purchases)>(@"
            SELECT SUM(CASE TUR WHEN 'SA' THEN ARA_TOPLAM END), SUM(CASE TUR WHEN 'AL' THEN ARA_TOPLAM END)
            FROM t_faturalar WHERE FIRMA_ID = @firm AND IPTAL_MI = 0 AND TARIH BETWEEN @start AND @end", args);

        var byCategory = (await conn.QueryAsync<(string Type, string Category, decimal Amount)>(@"
            SELECT h.TUR, k.AD, SUM(h.TUTAR - h.KDV_TUTARI)
            FROM t_hareketler h JOIN t_kategoriler k ON k.KATEGORI_ID = h.KATEGORI_ID
            WHERE h.FIRMA_ID = @firm AND h.IPTAL_MI = 0 AND h.TUR IN ('GI', 'GE') AND h.TARIH BETWEEN @start AND @end
            GROUP BY h.TUR, k.AD ORDER BY 3 DESC", args)).ToList();

        var income = byCategory.Where(x => x.Type == "GE").Select(x => new CategoryAmount(x.Category, x.Amount)).ToList();
        var expenses = byCategory.Where(x => x.Type == "GI").Select(x => new CategoryAmount(x.Category, x.Amount)).ToList();
        var sales = inv.Sales ?? 0;
        var purchases = inv.Purchases ?? 0;
        var gross = sales - purchases;

        return new ProfitLossDto(start, end, sales, purchases, gross, income.Sum(x => x.Amount), income,
            expenses.Sum(x => x.Amount), expenses, gross + income.Sum(x => x.Amount) - expenses.Sum(x => x.Amount));
    }

    /// <summary>KDV özeti: hesaplanan (satış) − indirilecek (alış + gider KDV'si) = ödenecek; eksiyse devreden KDV.</summary>
    public async Task<VatDto> VatAsync(long firm, int? year, int? month)
    {
        var y = year ?? DateTime.Today.Year;
        var m = month ?? DateTime.Today.Month;
        if (y < 2000 || y > 2100 || m < 1 || m > 12)
            throw new AppException("Geçerli bir yıl ve ay seçin.");

        var start = new DateTime(y, m, 1);
        var args = new { firm, start, end = start.AddMonths(1).AddDays(-1) };

        await using var conn = await db.OpenAsync();
        var rates = (await conn.QueryAsync<(string Type, decimal Rate, decimal Base, decimal Vat)>(@"
            SELECT f.TUR, s.KDV_ORANI, SUM(s.TUTAR), SUM(s.KDV_TUTARI)
            FROM t_fatura_satirlari s JOIN t_faturalar f ON f.FATURA_ID = s.FATURA_ID
            WHERE f.FIRMA_ID = @firm AND f.IPTAL_MI = 0 AND f.TARIH BETWEEN @start AND @end
            GROUP BY f.TUR, s.KDV_ORANI ORDER BY s.KDV_ORANI", args)).ToList();
        var expenseVat = await conn.ExecuteScalarAsync<decimal?>(@"
            SELECT SUM(KDV_TUTARI) FROM t_hareketler WHERE FIRMA_ID = @firm AND IPTAL_MI = 0 AND TUR = 'GI' AND TARIH BETWEEN @start AND @end", args) ?? 0;

        var sales = rates.Where(r => r.Type == "SA").Select(r => new VatRateRow(r.Rate, r.Base, r.Vat)).ToList();
        var purchases = rates.Where(r => r.Type == "AL").Select(r => new VatRateRow(r.Rate, r.Base, r.Vat)).ToList();
        var output = sales.Sum(x => x.Vat);
        var input = purchases.Sum(x => x.Vat);
        var diff = output - input - expenseVat;

        return new VatDto(y, m, output, sales, input, purchases, expenseVat, Math.Max(diff, 0), Math.Max(-diff, 0));
    }

    /// <summary>Alacak yaşlandırma: açık satış faturalarının kalan tutarları vadeye göre gruplanır.</summary>
    public async Task<IReadOnlyList<AgingRow>> AgingAsync(long firm)
    {
        await using var conn = await db.OpenAsync();
        return (await conn.QueryAsync<AgingRow>($@"
            SELECT c.CARI_ID AS CustomerId, c.UNVAN AS CustomerTitle,
                   SUM(CASE WHEN x.Days < 0 THEN x.Rem ELSE 0 END) AS NotDue,
                   SUM(CASE WHEN x.Days BETWEEN 0 AND 30 THEN x.Rem ELSE 0 END) AS D0To30,
                   SUM(CASE WHEN x.Days BETWEEN 31 AND 60 THEN x.Rem ELSE 0 END) AS D31To60,
                   SUM(CASE WHEN x.Days BETWEEN 61 AND 90 THEN x.Rem ELSE 0 END) AS D61To90,
                   SUM(CASE WHEN x.Days > 90 THEN x.Rem ELSE 0 END) AS Over90,
                   {Sql.CustomerBalance} AS Balance
            FROM (SELECT f.CARI_ID, DATEDIFF(CURDATE(), f.VADE_TARIHI) AS Days, f.GENEL_TOPLAM - {Sql.InvoicePaid} AS Rem
                  FROM t_faturalar f WHERE f.FIRMA_ID = @firm AND f.TUR = 'SA' AND f.IPTAL_MI = 0 AND f.GENEL_TOPLAM > {Sql.InvoicePaid}) x
            JOIN t_cariler c ON c.CARI_ID = x.CARI_ID
            GROUP BY c.CARI_ID, c.UNVAN, c.ACILIS_BAKIYESI, c.FIRMA_ID
            ORDER BY SUM(x.Rem) DESC", new { firm })).ToList();
    }
}

[Route("api/reports"), Authorize(Policy = Policies.FirmRead)]
public sealed class ReportsController(ReportService svc) : ApiControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard() => OkData(await svc.DashboardAsync(Me.Firm));

    [HttpGet("profit-loss")]
    public async Task<IActionResult> ProfitLoss(DateTime? from, DateTime? to) => OkData(await svc.ProfitLossAsync(Me.Firm, from, to));

    [HttpGet("vat")]
    public async Task<IActionResult> Vat(int? year, int? month) => OkData(await svc.VatAsync(Me.Firm, year, month));

    [HttpGet("aging")]
    public async Task<IActionResult> Aging() => OkData(await svc.AgingAsync(Me.Firm));
}
