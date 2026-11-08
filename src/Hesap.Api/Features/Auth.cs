using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Dapper;
using Hesap.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MySqlConnector;

namespace Hesap.Features;

public sealed record RegisterRequest(
    [Required, StringLength(200)] string CompanyName,
    [StringLength(11)] string? TaxNumber,
    [Required, StringLength(120)] string FullName,
    [Required, EmailAddress, StringLength(150)] string Email,
    [StringLength(30)] string? Phone,
    [Required, StringLength(128)] string Password);

public sealed record LoginRequest([Required, StringLength(150)] string Email, [Required, StringLength(128)] string Password);

public sealed record MobileLoginRequest([Required, StringLength(150)] string Email, [Required, StringLength(128)] string Password, [StringLength(120)] string? Device);

public sealed record RefreshRequest([Required, StringLength(200)] string RefreshToken);

public sealed record ForgotPasswordRequest([Required, EmailAddress, StringLength(150)] string Email);

public sealed record ResetPasswordRequest([Required, StringLength(200)] string Token, [Required, StringLength(128)] string NewPassword);

public sealed record ChangePasswordRequest([Required, StringLength(128)] string CurrentPassword, [Required, StringLength(128)] string NewPassword);

public sealed record MeDto(long UserId, string FullName, string Email, string Role, string RoleLabel, FirmSummaryDto? Firm, string AppName);

public sealed record FirmSummaryDto(long Id, string Name, string Plan, DateTime SubscriptionEnd, int DaysLeft, bool Expired, bool Suspended);

public sealed record MobileTokenDto(string AccessToken, string RefreshToken, int ExpiresIn, MeDto User);

public sealed class UserRow
{
    public long Id { get; set; }
    public long? FirmId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public bool Active { get; set; }
    public int Failed { get; set; }
    public bool Locked { get; set; }
}

public sealed class AuthService(Db db, IConfiguration config, Audit audit, EmailSender email, TokenIssuer tokens, ILogger<AuthService> logger)
{
    private const int MaxFailed = 5;
    private static readonly Lazy<string> DummyHash = new(() => Passwords.Hash(Guid.NewGuid().ToString()));
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    private string AppName => config["App:Name"] ?? "Hesap";

    private const string SelectUser = @"
        SELECT KULLANICI_ID AS Id, FIRMA_ID AS FirmId, AD_SOYAD AS FullName, EPOSTA AS Email, PAROLA_HASH AS PasswordHash,
               ROL AS Role, AKTIF_MI AS Active, HATALI_GIRIS AS Failed, KILITLI_MI AS Locked
        FROM t_kullanicilar";

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<long> RegisterAsync(RegisterRequest r)
    {
        var policy = PasswordPolicy.Validate(r.Password);
        if (policy != null)
            throw new AppException(policy);

        var tax = string.IsNullOrWhiteSpace(r.TaxNumber) ? null : r.TaxNumber.Trim();
        if (!TrIds.IsValidTaxOrNationalId(tax))
            throw new AppException("Geçerli bir vergi no (10 hane) veya T.C. kimlik no (11 hane) girin.");

        var trialDays = config.GetValue("Subscription:TrialDays", 14);
        var emailNorm = NormalizeEmail(r.Email);

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM t_kullanicilar WHERE EPOSTA = @emailNorm", new { emailNorm }, tx) > 0)
            throw AppException.Conflict("Bu e-posta adresiyle zaten bir hesap var. Giriş yapın veya şifrenizi sıfırlayın.");

        var firmId = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO t_firmalar (UNVAN, VERGI_NO, TELEFON, EPOSTA, PAKET, ABONELIK_BITIS)
            VALUES (@Name, @Tax, @Phone, @Email, 'DE', @End);
            SELECT LAST_INSERT_ID();",
            new { Name = r.CompanyName.Trim(), Tax = tax, Phone = r.Phone?.Trim(), Email = emailNorm, End = DateTime.Today.AddDays(trialDays) }, tx);

        var userId = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO t_kullanicilar (FIRMA_ID, AD_SOYAD, EPOSTA, TELEFON, PAROLA_HASH, ROL)
            VALUES (@firmId, @FullName, @emailNorm, @Phone, @Hash, 'AD');
            SELECT LAST_INSERT_ID();",
            new { firmId, FullName = r.FullName.Trim(), emailNorm, Phone = r.Phone?.Trim(), Hash = Passwords.Hash(r.Password) }, tx);

        // Yeni firmaya hazır kasa ve sık kullanılan gelir/gider kategorileri.
        await conn.ExecuteAsync("INSERT INTO t_hesaplar (FIRMA_ID, TUR, AD) VALUES (@firmId, 'KA', 'Merkez Kasa')", new { firmId }, tx);
        var categories = new[]
        {
            ("GI", "Kira"), ("GI", "Personel / Maaş"), ("GI", "Elektrik, Su, Doğalgaz"), ("GI", "İnternet ve Telefon"),
            ("GI", "Yakıt ve Ulaşım"), ("GI", "Kırtasiye ve Ofis"), ("GI", "Vergi ve SGK"), ("GI", "Banka Masrafları"), ("GI", "Diğer Giderler"),
            ("GE", "Faiz Geliri"), ("GE", "Diğer Gelirler")
        };
        await conn.ExecuteAsync("INSERT INTO t_kategoriler (FIRMA_ID, TUR, AD) VALUES (@firmId, @Tur, @Ad)",
            categories.Select(c => new { firmId, Tur = c.Item1, Ad = c.Item2 }), tx);

        await tx.CommitAsync();

        await audit.LogAsync(new CurrentUser { UserId = userId, FirmId = firmId, Role = Roles.Admin }, "FIRMA_KAYIT", "Firma", firmId, r.CompanyName.Trim());
        return userId;
    }

    /// <summary>
    /// Kimlik doğrulama: başarısız denemeler sayılır, 5. hatada hesap kilitlenir (şifre sıfırlama ile açılır).
    /// Hatalı e-posta ve hatalı şifre aynı mesajı verir (kullanıcı varlığı sızmaz).
    /// </summary>
    public async Task<UserRow> AuthenticateAsync(string emailInput, string password)
    {
        const string invalid = "E-posta veya şifre hatalı.";
        var emailNorm = NormalizeEmail(emailInput);

        await using var conn = await db.OpenAsync();
        var user = await conn.QuerySingleOrDefaultAsync<UserRow>($"{SelectUser} WHERE EPOSTA = @emailNorm", new { emailNorm });

        if (user == null || !user.Active)
        {
            // Zamanlama farkıyla kullanıcı varlığı anlaşılmasın diye yine gerçek bir özet doğrulanır.
            Passwords.Verify(DummyHash.Value, password);
            throw new AppException(invalid, StatusCodes.Status401Unauthorized);
        }

        if (user.Locked)
            throw new AppException("Hesabınız çok sayıda hatalı deneme nedeniyle kilitlendi. \"Şifremi unuttum\" ile yeni şifre belirleyin.", StatusCodes.Status423Locked);

        if (!Passwords.Verify(user.PasswordHash, password))
        {
            var failed = user.Failed + 1;
            await conn.ExecuteAsync("UPDATE t_kullanicilar SET HATALI_GIRIS = @failed, KILITLI_MI = @locked WHERE KULLANICI_ID = @Id",
                new { failed, locked = failed >= MaxFailed, user.Id });

            if (failed >= MaxFailed)
            {
                await audit.LogAsync(new CurrentUser { UserId = user.Id, FirmId = user.FirmId, Role = user.Role }, "HESAP_KILITLENDI", "Kullanici", user.Id);
                throw new AppException("Hesabınız çok sayıda hatalı deneme nedeniyle kilitlendi. \"Şifremi unuttum\" ile yeni şifre belirleyin.", StatusCodes.Status423Locked);
            }

            throw new AppException(invalid, StatusCodes.Status401Unauthorized);
        }

        if (user.FirmId.HasValue)
        {
            var status = await conn.ExecuteScalarAsync<string>("SELECT DURUM FROM t_firmalar WHERE FIRMA_ID = @FirmId", new { user.FirmId });
            if (status != "AK")
                throw new AppException("Firma hesabınız askıya alınmış. Lütfen bizimle iletişime geçin.", StatusCodes.Status403Forbidden);
        }

        await conn.ExecuteAsync("UPDATE t_kullanicilar SET HATALI_GIRIS = 0, SON_GIRIS = NOW() WHERE KULLANICI_ID = @Id", new { user.Id });
        await audit.LogAsync(new CurrentUser { UserId = user.Id, FirmId = user.FirmId, Role = user.Role }, "GIRIS", "Kullanici", user.Id);
        return user;
    }

    public static ClaimsPrincipal Principal(long userId, long? firmId, string role, string name)
    {
        var claims = new List<Claim>
        {
            new(Claims.UserId, userId.ToString()),
            new(ClaimTypes.Role, role),
            new(ClaimTypes.Name, name)
        };
        if (firmId.HasValue)
            claims.Add(new Claim(Claims.FirmId, firmId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public async Task<MeDto> GetMeAsync(long userId)
    {
        await using var conn = await db.OpenAsync();
        var user = await conn.QuerySingleOrDefaultAsync<UserRow>($"{SelectUser} WHERE KULLANICI_ID = @userId", new { userId })
                   ?? throw new AppException("Oturum geçersiz.", StatusCodes.Status401Unauthorized);

        FirmSummaryDto? firm = null;
        if (user.FirmId.HasValue)
        {
            var f = await conn.QuerySingleAsync<(long Id, string Name, string Plan, DateTime End, string Status)>(
                "SELECT FIRMA_ID, UNVAN, PAKET, ABONELIK_BITIS, DURUM FROM t_firmalar WHERE FIRMA_ID = @FirmId", new { user.FirmId });
            var daysLeft = (f.End.Date - DateTime.Today).Days;
            firm = new FirmSummaryDto(f.Id, f.Name, f.Plan, f.End, Math.Max(daysLeft, 0), daysLeft < 0, f.Status != "AK");
        }

        return new MeDto(user.Id, user.FullName, user.Email, user.Role, Roles.Label(user.Role), firm, AppName);
    }

    // ------------------------------------------------------------------ Mobil belirteçler

    public async Task<MobileTokenDto> IssueMobileAsync(UserRow user, string? device)
    {
        var refresh = Passwords.RandomToken(48);

        await using var conn = await db.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO t_mobil_oturumlar (KULLANICI_ID, TOKEN_HASH, CIHAZ, BITIS) VALUES (@Id, @Hash, @Device, @End)",
            new { user.Id, Hash = Passwords.Sha256Hex(refresh), Device = device?.Trim(), End = DateTime.UtcNow.Add(RefreshLifetime) });

        return new MobileTokenDto(tokens.Issue(user.Id, user.FirmId, user.Role, user.FullName), refresh,
            (int)TokenIssuer.AccessLifetime.TotalSeconds, await GetMeAsync(user.Id));
    }

    /// <summary>Yenileme belirteci tek kullanımlıktır: her yenilemede eskisi iptal edilip yenisi verilir.</summary>
    public async Task<MobileTokenDto> RefreshMobileAsync(string refreshToken)
    {
        await using var conn = await db.OpenAsync();
        var hash = Passwords.Sha256Hex(refreshToken);

        var session = await conn.QuerySingleOrDefaultAsync<(long Id, long UserId, string? Device)>(@"
            SELECT OTURUM_ID, KULLANICI_ID, CIHAZ FROM t_mobil_oturumlar
            WHERE TOKEN_HASH = @hash AND IPTAL_MI = 0 AND BITIS > UTC_TIMESTAMP()", new { hash });

        if (session.Id == 0)
            throw new AppException("Oturumunuzun süresi doldu. Lütfen tekrar giriş yapın.", StatusCodes.Status401Unauthorized);

        var revoked = await conn.ExecuteAsync("UPDATE t_mobil_oturumlar SET IPTAL_MI = 1 WHERE OTURUM_ID = @Id AND IPTAL_MI = 0", new { session.Id });
        if (revoked == 0)
            throw new AppException("Oturumunuzun süresi doldu. Lütfen tekrar giriş yapın.", StatusCodes.Status401Unauthorized);

        var user = await conn.QuerySingleOrDefaultAsync<UserRow>($"{SelectUser} WHERE KULLANICI_ID = @UserId", new { session.UserId });
        if (user == null || !user.Active || user.Locked)
            throw new AppException("Oturumunuzun süresi doldu. Lütfen tekrar giriş yapın.", StatusCodes.Status401Unauthorized);

        return await IssueMobileAsync(user, session.Device);
    }

    public async Task RevokeMobileAsync(string refreshToken)
    {
        await using var conn = await db.OpenAsync();
        await conn.ExecuteAsync("UPDATE t_mobil_oturumlar SET IPTAL_MI = 1 WHERE TOKEN_HASH = @Hash", new { Hash = Passwords.Sha256Hex(refreshToken) });
    }

    // ------------------------------------------------------------------ Şifre

    public async Task ForgotPasswordAsync(string emailInput, string baseUrl)
    {
        var emailNorm = NormalizeEmail(emailInput);
        await using var conn = await db.OpenAsync();
        var user = await conn.QuerySingleOrDefaultAsync<UserRow>($"{SelectUser} WHERE EPOSTA = @emailNorm AND AKTIF_MI = 1", new { emailNorm });
        if (user == null)
            return;

        var token = Passwords.RandomToken();
        await conn.ExecuteAsync("INSERT INTO t_parola_sifirlama (KULLANICI_ID, TOKEN_HASH, BITIS) VALUES (@Id, @Hash, @End)",
            new { user.Id, Hash = Passwords.Sha256Hex(token), End = DateTime.UtcNow.AddMinutes(30) });

        // Bağlantı yapılandırılmış genel adresle kurulur (Host başlığı zehirlenmesine karşı).
        var root = (string.IsNullOrWhiteSpace(config["App:PublicBaseUrl"]) ? baseUrl : config["App:PublicBaseUrl"]!).TrimEnd('/');
        var link = $"{root}/reset-password?token={Uri.EscapeDataString(token)}";
        var body = $"Merhaba {user.FullName},\n\n{AppName} şifre sıfırlama talebiniz alındı. Aşağıdaki bağlantıyla yeni şifrenizi belirleyebilirsiniz (30 dakika geçerlidir):\n\n{link}\n\nBu talebi siz yapmadıysanız bu e-postayı yok sayabilirsiniz.";

        // Arka planda gönderilir: kayıtlı/kayıtsız adres için yanıt aynı sürede döner, SMTP hatası kullanıcıya yansımaz.
        _ = Task.Run(async () =>
        {
            try { await email.SendAsync(user.Email, $"{AppName} — Şifre sıfırlama", body); }
            catch (Exception ex) { logger.LogError(ex, "Şifre sıfırlama e-postası gönderilemedi (kullanıcı {Id}).", user.Id); }
        });
    }

    public async Task ResetPasswordAsync(string token, string newPassword)
    {
        var policy = PasswordPolicy.Validate(newPassword);
        if (policy != null)
            throw new AppException(policy);

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        var row = await conn.QuerySingleOrDefaultAsync<(long Id, long UserId)>(@"
            SELECT ID, KULLANICI_ID FROM t_parola_sifirlama
            WHERE TOKEN_HASH = @Hash AND KULLANILDI IS NULL AND BITIS > UTC_TIMESTAMP()
            FOR UPDATE", new { Hash = Passwords.Sha256Hex(token) }, tx);

        if (row.Id == 0)
            throw new AppException("Bağlantı geçersiz veya süresi dolmuş. Yeni bir sıfırlama bağlantısı isteyin.");

        await conn.ExecuteAsync("UPDATE t_parola_sifirlama SET KULLANILDI = UTC_TIMESTAMP() WHERE ID = @Id", new { row.Id }, tx);
        // Sıfırlama e-posta sahipliğini kanıtlar: kilit açılır, tüm mobil oturumlar kapatılır.
        await conn.ExecuteAsync("UPDATE t_kullanicilar SET PAROLA_HASH = @Hash, HATALI_GIRIS = 0, KILITLI_MI = 0 WHERE KULLANICI_ID = @UserId",
            new { Hash = Passwords.Hash(newPassword), row.UserId }, tx);
        await conn.ExecuteAsync("UPDATE t_mobil_oturumlar SET IPTAL_MI = 1 WHERE KULLANICI_ID = @UserId", new { row.UserId }, tx);
        await tx.CommitAsync();

        await audit.LogAsync(new CurrentUser { UserId = row.UserId }, "SIFRE_SIFIRLANDI", "Kullanici", row.UserId);
    }

    public async Task ChangePasswordAsync(CurrentUser me, string current, string newPassword)
    {
        var policy = PasswordPolicy.Validate(newPassword);
        if (policy != null)
            throw new AppException(policy);

        await using var conn = await db.OpenAsync();
        var hash = await conn.ExecuteScalarAsync<string>("SELECT PAROLA_HASH FROM t_kullanicilar WHERE KULLANICI_ID = @UserId", new { me.UserId });
        if (hash == null || !Passwords.Verify(hash, current))
            throw new AppException("Mevcut şifreniz hatalı.");

        await conn.ExecuteAsync("UPDATE t_kullanicilar SET PAROLA_HASH = @Hash WHERE KULLANICI_ID = @UserId", new { Hash = Passwords.Hash(newPassword), me.UserId });
        await audit.LogAsync(me, "SIFRE_DEGISTI", "Kullanici", me.UserId);
    }
}

[Route("api/auth")]
public sealed class AuthController(AuthService auth) : ApiControllerBase
{
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest r)
    {
        var userId = await auth.RegisterAsync(r);
        var me = await auth.GetMeAsync(userId);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthService.Principal(me.UserId, me.Firm?.Id, me.Role, me.FullName));
        return OkData(me, $"Hoş geldiniz! {me.Firm!.DaysLeft} günlük deneme süreniz başladı.");
    }

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest r)
    {
        var user = await auth.AuthenticateAsync(r.Email, r.Password);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthService.Principal(user.Id, user.FirmId, user.Role, user.FullName));
        return OkData(await auth.GetMeAsync(user.Id));
    }

    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return OkMessage("Çıkış yapıldı.");
    }

    [HttpGet("me"), Authorize]
    public async Task<IActionResult> MeInfo() => OkData(await auth.GetMeAsync(Me.UserId));

    [HttpPost("forgot-password"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Forgot(ForgotPasswordRequest r)
    {
        await auth.ForgotPasswordAsync(r.Email, $"{Request.Scheme}://{Request.Host}");
        return OkMessage("Bu e-posta adresi kayıtlıysa şifre sıfırlama bağlantısı gönderildi.");
    }

    [HttpPost("reset-password"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Reset(ResetPasswordRequest r)
    {
        await auth.ResetPasswordAsync(r.Token, r.NewPassword);
        return OkMessage("Şifreniz değiştirildi. Yeni şifrenizle giriş yapabilirsiniz.");
    }

    [HttpPost("change-password"), Authorize, AllowExpiredSubscription]
    public async Task<IActionResult> Change(ChangePasswordRequest r)
    {
        await auth.ChangePasswordAsync(Me, r.CurrentPassword, r.NewPassword);
        return OkMessage("Şifreniz değiştirildi.");
    }

    // ------------------------------------------------------------------ Mobil

    [HttpPost("mobile/login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> MobileLogin(MobileLoginRequest r)
    {
        var user = await auth.AuthenticateAsync(r.Email, r.Password);
        if (user.FirmId == null)
            throw AppException.Forbidden("Mobil uygulama firma hesaplarıyla kullanılır.");
        return OkData(await auth.IssueMobileAsync(user, r.Device));
    }

    [HttpPost("mobile/refresh"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> MobileRefresh(RefreshRequest r) => OkData(await auth.RefreshMobileAsync(r.RefreshToken));

    [HttpPost("mobile/logout"), AllowAnonymous, AllowExpiredSubscription]
    public async Task<IActionResult> MobileLogout(RefreshRequest r)
    {
        await auth.RevokeMobileAsync(r.RefreshToken);
        return OkMessage("Çıkış yapıldı.");
    }
}
