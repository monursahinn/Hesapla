using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.IdentityModel.Tokens;

namespace Hesap.Infrastructure;

/// <summary>Denetim kaydı: kim, ne zaman, hangi kayıtta ne yaptı. Hata olursa işlemi bozmaz.</summary>
public sealed class Audit(Db db, IHttpContextAccessor http, ILogger<Audit> logger)
{
    public async Task LogAsync(CurrentUser? me, string action, string? targetType = null, long? targetId = null, string? description = null)
    {
        try
        {
            await using var conn = await db.OpenAsync();
            await conn.ExecuteAsync(@"
                INSERT INTO t_denetim (FIRMA_ID, KULLANICI_ID, ISLEM, HEDEF_TUR, HEDEF_ID, ACIKLAMA, IP)
                VALUES (@FirmId, @UserId, @Action, @TargetType, @TargetId, @Description, @Ip)",
                new
                {
                    FirmId = me?.FirmId,
                    UserId = me?.UserId > 0 ? me.UserId : (long?)null,
                    Action = action,
                    TargetType = targetType,
                    TargetId = targetId,
                    Description = description is { Length: > 500 } ? description[..500] : description,
                    Ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString()
                });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Denetim kaydı yazılamadı ({Action}).", action);
        }
    }
}

public sealed class EmailSender(IConfiguration config, ILogger<EmailSender> logger)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Smtp:Host"]) && !string.IsNullOrWhiteSpace(config["Smtp:FromEmail"]);

    public async Task SendAsync(string to, string subject, string body)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("SMTP tanımlı değil; e-posta gönderilmedi: {Subject}", subject);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(config["Smtp:FromEmail"]!, config["Smtp:FromName"] ?? config["App:Name"] ?? "Hesap"),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(to);

        using var client = new SmtpClient(config["Smtp:Host"], int.TryParse(config["Smtp:Port"], out var p) ? p : 587)
        {
            EnableSsl = !bool.TryParse(config["Smtp:EnableSsl"], out var ssl) || ssl,
            Credentials = new NetworkCredential(config["Smtp:Username"], config["Smtp:Password"])
        };

        await client.SendMailAsync(message);
    }
}

/// <summary>Mobil uygulama için JWT erişim belirteci (kısa ömürlü).</summary>
public sealed class TokenIssuer(IConfiguration config)
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(30);

    public string Issue(long userId, long? firmId, string role, string name)
    {
        var claims = new List<Claim>
        {
            new(Claims.UserId, userId.ToString()),
            new(ClaimTypes.Role, role),
            new(ClaimTypes.Name, name)
        };
        if (firmId.HasValue)
            claims.Add(new Claim(Claims.FirmId, firmId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            issuer: "hesap",
            audience: "hesap-mobile",
            claims: claims,
            expires: DateTime.UtcNow.Add(AccessLifetime),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Abonelik denetimi: süresi dolmuş veya askıya alınmış firmada kayıt değiştiren istekler (GET dışı) reddedilir;
/// veriler okunabilir ve dışa aktarılabilir kalır. Platform yöneticisi ve kimlik/abonelik uçları muaf.
/// </summary>
public sealed class SubscriptionFilter(Db db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var http = ctx.HttpContext;
        var me = CurrentUser.From(http.User);

        var exempt = HttpMethods.IsGet(http.Request.Method) || me.FirmId == null ||
                     ctx.ActionDescriptor.EndpointMetadata.OfType<AllowExpiredSubscriptionAttribute>().Any();

        if (!exempt)
        {
            await using var conn = await db.OpenAsync();
            var firm = await conn.QuerySingleOrDefaultAsync<(string Durum, DateTime Bitis)>(
                "SELECT DURUM, ABONELIK_BITIS FROM t_firmalar WHERE FIRMA_ID = @Id", new { Id = me.FirmId });

            if (firm.Durum != "AK")
                throw new AppException("Firma hesabınız askıya alınmış. Lütfen bizimle iletişime geçin.", StatusCodes.Status402PaymentRequired);

            if (firm.Bitis.Date < DateTime.Today)
                throw new AppException("Abonelik süreniz doldu. Verilerinizi görebilir ve dışa aktarabilirsiniz; yeni kayıt için aboneliğinizi yenileyin.", StatusCodes.Status402PaymentRequired);
        }

        await next();
    }
}

/// <summary>Abonelik süresi dolsa da çalışması gereken uçlar (çıkış, şifre değiştirme).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowExpiredSubscriptionAttribute : Attribute;

public static class Json
{
    public static readonly JsonSerializerOptions Options = JsonSerializerOptions.Web;
}
