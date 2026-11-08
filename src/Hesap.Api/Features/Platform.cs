using System.ComponentModel.DataAnnotations;
using Dapper;
using Hesap.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hesap.Features;

public sealed class PlatformFirmDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? TaxNumber { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Plan { get; set; } = "";
    public DateTime SubscriptionEnd { get; set; }
    public string Status { get; set; } = "";
    public int Users { get; set; }
    public int Invoices { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
    public bool Expired => SubscriptionEnd.Date < DateTime.Today;
}

public sealed record SubscriptionRequest([Required, StringLength(2)] string Plan, DateTime End);

public sealed record FirmStatusRequest(bool Active);

public sealed class AuditDto
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = "";
    public string? TargetType { get; set; }
    public long? TargetId { get; set; }
    public string? Description { get; set; }
    public string? Ip { get; set; }
}

public sealed class PlatformService(Db db, Audit audit)
{
    public async Task<PagedResult<PlatformFirmDto>> ListFirmsAsync(string? q, string? filter, PageQuery page)
    {
        const string where = @"WHERE (@like IS NULL OR f.UNVAN LIKE @like OR f.EPOSTA LIKE @like OR f.VERGI_NO LIKE @like)
              AND (@filter IS NULL
                   OR (@filter = 'trial' AND f.PAKET = 'DE' AND f.ABONELIK_BITIS >= CURDATE())
                   OR (@filter = 'paid' AND f.PAKET <> 'DE' AND f.ABONELIK_BITIS >= CURDATE())
                   OR (@filter = 'expired' AND f.ABONELIK_BITIS < CURDATE())
                   OR (@filter = 'suspended' AND f.DURUM = 'AS'))";
        var args = new { like = Sql.Like(q), filter = string.IsNullOrWhiteSpace(filter) ? null : filter, page.Offset, Size = page.SafeSize };

        await using var conn = await db.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM t_firmalar f {where}", args);
        var items = (await conn.QueryAsync<PlatformFirmDto>($@"
            SELECT f.FIRMA_ID AS Id, f.UNVAN AS Name, f.VERGI_NO AS TaxNumber, f.EPOSTA AS Email, f.TELEFON AS Phone, f.PAKET AS Plan,
                   f.ABONELIK_BITIS AS SubscriptionEnd, f.DURUM AS Status, f.KAYIT_TARIHI AS CreatedAt,
                   (SELECT COUNT(*) FROM t_kullanicilar k WHERE k.FIRMA_ID = f.FIRMA_ID AND k.AKTIF_MI = 1) AS Users,
                   (SELECT COUNT(*) FROM t_faturalar x WHERE x.FIRMA_ID = f.FIRMA_ID) AS Invoices,
                   (SELECT MAX(k.SON_GIRIS) FROM t_kullanicilar k WHERE k.FIRMA_ID = f.FIRMA_ID) AS LastLogin
            FROM t_firmalar f {where} ORDER BY f.KAYIT_TARIHI DESC LIMIT @Size OFFSET @Offset", args)).ToList();

        return new PagedResult<PlatformFirmDto>(items, total, page.SafePage, page.SafeSize);
    }

    public async Task<object> StatsAsync()
    {
        await using var conn = await db.OpenAsync();
        return await conn.QuerySingleAsync<(int Total, int Trial, int Paid, int Expired, int Suspended)>(@"
            SELECT COUNT(*),
                   SUM(PAKET = 'DE' AND ABONELIK_BITIS >= CURDATE()),
                   SUM(PAKET <> 'DE' AND ABONELIK_BITIS >= CURDATE()),
                   SUM(ABONELIK_BITIS < CURDATE()),
                   SUM(DURUM = 'AS')
            FROM t_firmalar") is var s
            ? new { total = s.Total, trial = s.Trial, paid = s.Paid, expired = s.Expired, suspended = s.Suspended }
            : new { };
    }

    public async Task SetSubscriptionAsync(CurrentUser me, long id, SubscriptionRequest r)
    {
        if (r.Plan is not ("DE" or "ST" or "PR"))
            throw new AppException("Paket seçin: deneme, standart veya profesyonel.");
        if (r.End.Year < 2000 || r.End > DateTime.Today.AddYears(10))
            throw new AppException("Abonelik bitiş tarihi geçerli değil.");

        await using var conn = await db.OpenAsync();
        if (await conn.ExecuteAsync("UPDATE t_firmalar SET PAKET = @Plan, ABONELIK_BITIS = @End WHERE FIRMA_ID = @id", new { r.Plan, End = r.End.Date, id }) == 0)
            throw AppException.NotFound("Firma");
        await audit.LogAsync(me, "PLATFORM_ABONELIK", "Firma", id, $"{r.Plan} {r.End:dd.MM.yyyy}");
    }

    public async Task SetStatusAsync(CurrentUser me, long id, bool active)
    {
        await using var conn = await db.OpenAsync();
        if (await conn.ExecuteAsync("UPDATE t_firmalar SET DURUM = @s WHERE FIRMA_ID = @id", new { s = active ? "AK" : "AS", id }) == 0)
            throw AppException.NotFound("Firma");
        // Askıya alınan firmanın mobil oturumları kapatılır.
        if (!active)
            await conn.ExecuteAsync("UPDATE t_mobil_oturumlar m JOIN t_kullanicilar k ON k.KULLANICI_ID = m.KULLANICI_ID SET m.IPTAL_MI = 1 WHERE k.FIRMA_ID = @id", new { id });
        await audit.LogAsync(me, active ? "PLATFORM_FIRMA_AKTIF" : "PLATFORM_FIRMA_ASKIYA", "Firma", id);
    }

    public async Task<PagedResult<AuditDto>> AuditAsync(long firm, PageQuery page)
    {
        await using var conn = await db.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM t_denetim WHERE FIRMA_ID = @firm", new { firm });
        var items = (await conn.QueryAsync<AuditDto>(@"
            SELECT d.DENETIM_ID AS Id, d.TARIH AS Date, k.AD_SOYAD AS UserName, d.ISLEM AS Action, d.HEDEF_TUR AS TargetType,
                   d.HEDEF_ID AS TargetId, d.ACIKLAMA AS Description, d.IP AS Ip
            FROM t_denetim d LEFT JOIN t_kullanicilar k ON k.KULLANICI_ID = d.KULLANICI_ID
            WHERE d.FIRMA_ID = @firm ORDER BY d.DENETIM_ID DESC LIMIT @Size OFFSET @Offset",
            new { firm, page.Offset, Size = page.SafeSize })).ToList();
        return new PagedResult<AuditDto>(items, total, page.SafePage, page.SafeSize);
    }
}

[Route("api/platform"), Authorize(Policy = Policies.Platform)]
public sealed class PlatformController(PlatformService svc) : ApiControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> Stats() => OkData(await svc.StatsAsync());

    [HttpGet("firms")]
    public async Task<IActionResult> Firms(string? q, string? filter, [FromQuery] PageQuery page) => OkData(await svc.ListFirmsAsync(q, filter, page));

    [HttpPut("firms/{id:long}/subscription")]
    public async Task<IActionResult> Subscription(long id, SubscriptionRequest r)
    {
        await svc.SetSubscriptionAsync(Me, id, r);
        return OkMessage("Abonelik güncellendi.");
    }

    [HttpPut("firms/{id:long}/status")]
    public async Task<IActionResult> Status(long id, FirmStatusRequest r)
    {
        await svc.SetStatusAsync(Me, id, r.Active);
        return OkMessage(r.Active ? "Firma aktifleştirildi." : "Firma askıya alındı.");
    }
}

[Route("api/audit"), Authorize(Policy = Policies.FirmAdmin)]
public sealed class AuditController(PlatformService svc) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageQuery page) => OkData(await svc.AuditAsync(Me.Firm, page));
}

public static class FeatureRegistration
{
    public static IServiceCollection AddFeatures(this IServiceCollection services) => services
        .AddScoped<AuthService>()
        .AddScoped<CompanyService>()
        .AddScoped<CustomerService>()
        .AddScoped<ProductService>()
        .AddScoped<AccountService>()
        .AddScoped<InvoiceService>()
        .AddScoped<MovementService>()
        .AddScoped<ReportService>()
        .AddScoped<InvoicePdf>()
        .AddScoped<ExcelExport>()
        .AddScoped<PlatformService>();
}
