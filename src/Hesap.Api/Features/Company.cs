using System.ComponentModel.DataAnnotations;
using Dapper;
using Hesap.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hesap.Features;

public sealed class FirmDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? TaxNumber { get; set; }
    public string? TaxOffice { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Iban { get; set; }
    public string? InvoiceNote { get; set; }
    public bool HasLogo { get; set; }
    public string Plan { get; set; } = "";
    public DateTime SubscriptionEnd { get; set; }
}

public sealed record FirmUpdateRequest(
    [Required, StringLength(200)] string Name,
    [StringLength(11)] string? TaxNumber,
    [StringLength(80)] string? TaxOffice,
    [StringLength(500)] string? Address,
    [StringLength(60)] string? City,
    [StringLength(80)] string? District,
    [StringLength(30)] string? Phone,
    [EmailAddress, StringLength(150)] string? Email,
    [StringLength(40)] string? Iban,
    [StringLength(500)] string? InvoiceNote);

public sealed class UserDto
{
    public long Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string Role { get; set; } = "";
    public string RoleLabel => Roles.Label(Role);
    public bool Active { get; set; }
    public bool Locked { get; set; }
    public DateTime? LastLogin { get; set; }
}

public sealed record UserCreateRequest(
    [Required, StringLength(120)] string FullName,
    [Required, EmailAddress, StringLength(150)] string Email,
    [StringLength(30)] string? Phone,
    [Required, StringLength(2)] string Role,
    [Required, StringLength(128)] string Password);

public sealed record UserUpdateRequest(
    [Required, StringLength(120)] string FullName,
    [StringLength(30)] string? Phone,
    [Required, StringLength(2)] string Role,
    bool Active);

public sealed class CompanyService(Db db, Audit audit, IConfiguration config, IWebHostEnvironment env)
{
    private const string FirmSelect = @"
        SELECT FIRMA_ID AS Id, UNVAN AS Name, VERGI_NO AS TaxNumber, VERGI_DAIRESI AS TaxOffice, ADRES AS Address, IL AS City,
               ILCE AS District, TELEFON AS Phone, EPOSTA AS Email, IBAN AS Iban, FATURA_NOTU AS InvoiceNote,
               LOGO_DOSYA IS NOT NULL AS HasLogo, PAKET AS Plan, ABONELIK_BITIS AS SubscriptionEnd
        FROM t_firmalar";

    public string LogoDir => Path.Combine(env.ContentRootPath, "App_Data", "logos");

    public async Task<FirmDto> GetFirmAsync(long firmId)
    {
        await using var conn = await db.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<FirmDto>($"{FirmSelect} WHERE FIRMA_ID = @firmId", new { firmId })
               ?? throw AppException.NotFound("Firma");
    }

    public async Task UpdateFirmAsync(CurrentUser me, FirmUpdateRequest r)
    {
        var tax = Clean(r.TaxNumber);
        if (!TrIds.IsValidTaxOrNationalId(tax))
            throw new AppException("Geçerli bir vergi no (10 hane) veya T.C. kimlik no (11 hane) girin.");

        var iban = TrIds.NormalizeIban(r.Iban);
        if (iban != null && !TrIds.IsValidTrIban(iban))
            throw new AppException("Geçerli bir TR IBAN girin (TR ile başlayan 26 karakter).");

        await using var conn = await db.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE t_firmalar SET UNVAN = @Name, VERGI_NO = @tax, VERGI_DAIRESI = @TaxOffice, ADRES = @Address, IL = @City, ILCE = @District,
                   TELEFON = @Phone, EPOSTA = @Email, IBAN = @iban, FATURA_NOTU = @InvoiceNote
            WHERE FIRMA_ID = @Firm",
            new
            {
                Name = r.Name.Trim(), tax, TaxOffice = Clean(r.TaxOffice), Address = Clean(r.Address), City = Clean(r.City),
                District = Clean(r.District), Phone = Clean(r.Phone), Email = Clean(r.Email), iban, InvoiceNote = Clean(r.InvoiceNote), me.Firm
            });

        await audit.LogAsync(me, "FIRMA_GUNCELLE", "Firma", me.Firm);
    }

    /// <summary>Logo: yalnızca PNG/JPEG, en fazla 1 MB; içerik gerçekten resim mi diye ilk baytlarından kontrol edilir.</summary>
    public async Task SaveLogoAsync(CurrentUser me, IFormFile? file)
    {
        if (file == null || file.Length == 0)
            throw new AppException("Logo dosyası seçin.");
        if (file.Length > 1024 * 1024)
            throw new AppException("Logo en fazla 1 MB olabilir.");

        var header = new byte[8];
        await using (var s = file.OpenReadStream())
            _ = await s.ReadAsync(header);

        var ext = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 ? ".png"
                : header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF ? ".jpg"
                : null;
        if (ext == null)
            throw new AppException("Logo PNG veya JPEG resmi olmalıdır.");

        Directory.CreateDirectory(LogoDir);
        var name = $"{me.Firm}-{Guid.NewGuid():N}{ext}";
        await using (var fs = new FileStream(Path.Combine(LogoDir, name), FileMode.CreateNew))
            await file.CopyToAsync(fs);

        await using var conn = await db.OpenAsync();
        var old = await conn.ExecuteScalarAsync<string?>("SELECT LOGO_DOSYA FROM t_firmalar WHERE FIRMA_ID = @Firm", new { me.Firm });
        await conn.ExecuteAsync("UPDATE t_firmalar SET LOGO_DOSYA = @name WHERE FIRMA_ID = @Firm", new { name, me.Firm });

        if (old != null)
            File.Delete(Path.Combine(LogoDir, Path.GetFileName(old)));

        await audit.LogAsync(me, "FIRMA_LOGO", "Firma", me.Firm);
    }

    public async Task<string?> GetLogoPathAsync(long firmId)
    {
        await using var conn = await db.OpenAsync();
        var name = await conn.ExecuteScalarAsync<string?>("SELECT LOGO_DOSYA FROM t_firmalar WHERE FIRMA_ID = @firmId", new { firmId });
        if (name == null)
            return null;
        var path = Path.Combine(LogoDir, Path.GetFileName(name));
        return File.Exists(path) ? path : null;
    }

    // ------------------------------------------------------------------ Kullanıcılar

    private const string UserSelect = @"
        SELECT KULLANICI_ID AS Id, AD_SOYAD AS FullName, EPOSTA AS Email, TELEFON AS Phone, ROL AS Role,
               AKTIF_MI AS Active, KILITLI_MI AS Locked, SON_GIRIS AS LastLogin
        FROM t_kullanicilar";

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(long firmId)
    {
        await using var conn = await db.OpenAsync();
        return (await conn.QueryAsync<UserDto>($"{UserSelect} WHERE FIRMA_ID = @firmId ORDER BY AKTIF_MI DESC, AD_SOYAD", new { firmId })).ToList();
    }

    private int UserLimit(string plan) => plan switch
    {
        "PR" => config.GetValue("Subscription:UserLimit:PR", 20),
        "ST" => config.GetValue("Subscription:UserLimit:ST", 5),
        _ => config.GetValue("Subscription:UserLimit:DE", 3)
    };

    private static void CheckRole(string role)
    {
        if (!Roles.FirmRoles.Contains(role))
            throw new AppException("Geçersiz rol. Yönetici, Muhasebe veya Görüntüleyici seçin.");
    }

    public async Task<long> CreateUserAsync(CurrentUser me, UserCreateRequest r)
    {
        CheckRole(r.Role);
        var policy = PasswordPolicy.Validate(r.Password);
        if (policy != null)
            throw new AppException(policy);

        await using var conn = await db.OpenAsync();
        var plan = await conn.ExecuteScalarAsync<string>("SELECT PAKET FROM t_firmalar WHERE FIRMA_ID = @Firm", new { me.Firm });
        var active = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM t_kullanicilar WHERE FIRMA_ID = @Firm AND AKTIF_MI = 1", new { me.Firm });
        if (active >= UserLimit(plan!))
            throw new AppException($"Paketinizde en fazla {UserLimit(plan!)} aktif kullanıcı olabilir. Daha fazlası için paketinizi yükseltin.");

        var email = AuthService.NormalizeEmail(r.Email);
        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM t_kullanicilar WHERE EPOSTA = @email", new { email }) > 0)
            throw AppException.Conflict("Bu e-posta adresi başka bir hesapta kullanılıyor.");

        var id = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO t_kullanicilar (FIRMA_ID, AD_SOYAD, EPOSTA, TELEFON, PAROLA_HASH, ROL)
            VALUES (@Firm, @FullName, @email, @Phone, @Hash, @Role);
            SELECT LAST_INSERT_ID();",
            new { me.Firm, FullName = r.FullName.Trim(), email, Phone = Clean(r.Phone), Hash = Passwords.Hash(r.Password), r.Role });

        await audit.LogAsync(me, "KULLANICI_EKLE", "Kullanici", id, $"{email} ({Roles.Label(r.Role)})");
        return id;
    }

    public async Task UpdateUserAsync(CurrentUser me, long id, UserUpdateRequest r)
    {
        CheckRole(r.Role);

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        var current = await conn.QuerySingleOrDefaultAsync<(string Role, bool Active)>(
            "SELECT ROL, AKTIF_MI FROM t_kullanicilar WHERE KULLANICI_ID = @id AND FIRMA_ID = @Firm FOR UPDATE", new { id, me.Firm }, tx);
        if (current.Role == null)
            throw AppException.NotFound("Kullanıcı");

        if (id == me.UserId && (r.Role != Roles.Admin || !r.Active))
            throw new AppException("Kendi yönetici yetkinizi kaldıramaz veya hesabınızı pasifleştiremezsiniz.");

        // Firmada en az bir aktif yönetici kalmalı.
        if (current.Role == Roles.Admin && current.Active && (r.Role != Roles.Admin || !r.Active))
        {
            var admins = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM t_kullanicilar WHERE FIRMA_ID = @Firm AND ROL = 'AD' AND AKTIF_MI = 1 FOR UPDATE", new { me.Firm }, tx);
            if (admins <= 1)
                throw new AppException("Firmada en az bir aktif yönetici kalmalıdır.");
        }

        if (r.Active && !current.Active)
        {
            var plan = await conn.ExecuteScalarAsync<string>("SELECT PAKET FROM t_firmalar WHERE FIRMA_ID = @Firm", new { me.Firm }, tx);
            var active = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM t_kullanicilar WHERE FIRMA_ID = @Firm AND AKTIF_MI = 1", new { me.Firm }, tx);
            if (active >= UserLimit(plan!))
                throw new AppException($"Paketinizde en fazla {UserLimit(plan!)} aktif kullanıcı olabilir.");
        }

        await conn.ExecuteAsync(@"
            UPDATE t_kullanicilar SET AD_SOYAD = @FullName, TELEFON = @Phone, ROL = @Role, AKTIF_MI = @Active
            WHERE KULLANICI_ID = @id AND FIRMA_ID = @Firm",
            new { FullName = r.FullName.Trim(), Phone = Clean(r.Phone), r.Role, r.Active, id, me.Firm }, tx);

        // Pasifleştirilen kullanıcının mobil oturumları kapatılır.
        if (!r.Active)
            await conn.ExecuteAsync("UPDATE t_mobil_oturumlar SET IPTAL_MI = 1 WHERE KULLANICI_ID = @id", new { id }, tx);

        await tx.CommitAsync();
        await audit.LogAsync(me, "KULLANICI_GUNCELLE", "Kullanici", id, $"{Roles.Label(r.Role)}, {(r.Active ? "aktif" : "pasif")}");
    }

    public async Task UnlockUserAsync(CurrentUser me, long id)
    {
        await using var conn = await db.OpenAsync();
        var n = await conn.ExecuteAsync("UPDATE t_kullanicilar SET KILITLI_MI = 0, HATALI_GIRIS = 0 WHERE KULLANICI_ID = @id AND FIRMA_ID = @Firm", new { id, me.Firm });
        if (n == 0)
            throw AppException.NotFound("Kullanıcı");
        await audit.LogAsync(me, "KULLANICI_KILIT_AC", "Kullanici", id);
    }

    internal static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

[Route("api/firm")]
public sealed class FirmController(CompanyService svc) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> Get() => OkData(await svc.GetFirmAsync(Me.Firm));

    [HttpPut, Authorize(Policy = Policies.FirmAdmin)]
    public async Task<IActionResult> Update(FirmUpdateRequest r)
    {
        await svc.UpdateFirmAsync(Me, r);
        return OkData(await svc.GetFirmAsync(Me.Firm), "Firma bilgileri kaydedildi.");
    }

    [HttpPost("logo"), Authorize(Policy = Policies.FirmAdmin), RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Logo(IFormFile? file)
    {
        await svc.SaveLogoAsync(Me, file);
        return OkMessage("Logo kaydedildi.");
    }

    [HttpGet("logo"), Authorize(Policy = Policies.FirmRead)]
    public async Task<IActionResult> GetLogo()
    {
        var path = await svc.GetLogoPathAsync(Me.Firm);
        return path == null ? NotFound() : PhysicalFile(path, path.EndsWith(".png") ? "image/png" : "image/jpeg");
    }
}

[Route("api/users"), Authorize(Policy = Policies.FirmAdmin)]
public sealed class UsersController(CompanyService svc) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => OkData(await svc.ListUsersAsync(Me.Firm));

    [HttpPost]
    public async Task<IActionResult> Create(UserCreateRequest r)
    {
        var id = await svc.CreateUserAsync(Me, r);
        return OkData(new { id }, "Kullanıcı eklendi.");
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, UserUpdateRequest r)
    {
        await svc.UpdateUserAsync(Me, id, r);
        return OkMessage("Kullanıcı güncellendi.");
    }

    [HttpPost("{id:long}/unlock")]
    public async Task<IActionResult> Unlock(long id)
    {
        await svc.UnlockUserAsync(Me, id);
        return OkMessage("Kullanıcının kilidi açıldı.");
    }
}
