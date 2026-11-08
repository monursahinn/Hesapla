using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Hesapla.Infrastructure;

/// <summary>Tüm API yanıtlarının zarfı: { success, message, data, errors }.</summary>
public sealed record ApiResponse(bool Success, string Message, object? Data = null, IDictionary<string, string[]>? Errors = null);

/// <summary>
/// İş kuralı hatası. Servisler doğrulama/yetki/bulunamadı durumlarında fırlatır; ara katman bunu
/// { success:false, message } zarfına çevirir. Mesaj kullanıcıya gösterilir, bu yüzden Türkçe ve anlaşılır olmalı.
/// </summary>
public sealed class AppException(string message, int status = StatusCodes.Status400BadRequest) : Exception(message)
{
    public int Status { get; } = status;

    public static AppException NotFound(string what = "Kayıt") => new($"{what} bulunamadı.", StatusCodes.Status404NotFound);
    public static AppException Forbidden(string message = "Bu işlem için yetkiniz yok.") => new(message, StatusCodes.Status403Forbidden);
    public static AppException Conflict(string message) => new(message, StatusCodes.Status409Conflict);
}

/// <summary>Oturumdaki kullanıcı. Firma kimliği yalnızca kimlik bilgisinden (çerez/JWT) alınır; istemciden asla.</summary>
public sealed class CurrentUser
{
    public long UserId { get; init; }
    public long? FirmId { get; init; }
    public string Role { get; init; } = "";
    public string Name { get; init; } = "";

    public bool IsPlatform => Role == Roles.Platform;

    /// <summary>Firma verisine erişen her servis bunu kullanır; firmasız (platform) kullanıcı firma verisine erişemez.</summary>
    public long Firm => FirmId ?? throw AppException.Forbidden("Bu işlem bir firma hesabı gerektirir.");

    public static CurrentUser From(ClaimsPrincipal p) => new()
    {
        UserId = long.TryParse(p.FindFirstValue(Claims.UserId), out var u) ? u : 0,
        FirmId = long.TryParse(p.FindFirstValue(Claims.FirmId), out var f) ? f : null,
        Role = p.FindFirstValue(ClaimTypes.Role) ?? "",
        Name = p.FindFirstValue(ClaimTypes.Name) ?? ""
    };
}

public static class Claims
{
    public const string UserId = "uid";
    public const string FirmId = "fid";
}

public static class Roles
{
    public const string Platform = "SU";
    public const string Admin = "AD";
    public const string Accountant = "AC";
    public const string Viewer = "VW";

    public static readonly string[] FirmRoles = [Admin, Accountant, Viewer];

    public static string Label(string role) => role switch
    {
        Platform => "Platform yöneticisi",
        Admin => "Yönetici",
        Accountant => "Muhasebe",
        Viewer => "Görüntüleyici",
        _ => role
    };
}

public static class Policies
{
    /// <summary>Firma verisini görebilir (yönetici, muhasebe, görüntüleyici).</summary>
    public const string FirmRead = nameof(FirmRead);
    /// <summary>Kayıt girebilir (yönetici, muhasebe).</summary>
    public const string FirmWrite = nameof(FirmWrite);
    /// <summary>Firma ayarları ve kullanıcılar (yalnızca yönetici).</summary>
    public const string FirmAdmin = nameof(FirmAdmin);
    public const string Platform = nameof(Platform);
}

[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    private CurrentUser? _me;

    protected CurrentUser Me => _me ??= CurrentUser.From(User);

    protected IActionResult OkData(object? data, string message = "") => Ok(new ApiResponse(true, message, data));

    protected IActionResult OkMessage(string message) => Ok(new ApiResponse(true, message));

    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Sayfalı liste isteği; sayfa boyutu 1–100 ile sınırlanır.</summary>
public record PageQuery(int Page = 1, int PageSize = 25)
{
    public int SafePage => Math.Max(1, Page);
    public int SafeSize => Math.Clamp(PageSize, 1, 100);
    public int Offset => (SafePage - 1) * SafeSize;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
