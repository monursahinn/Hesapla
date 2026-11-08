using System.Net;
using System.Net.Http.Json;
using Hesap.Mobile.Models.Api;

namespace Hesap.Mobile.Services;

/// <summary>
/// Mobil oturum: e-posta + şifre ile giriş, 30 dakikalık erişim belirteci, 30 günlük tek kullanımlık
/// (her yenilemede değişen) yenileme belirteci.
/// </summary>
public sealed class AuthService
{
    // Kimlik doğrulama çağrıları belirteç eklemeyen ayrı bir HttpClient kullanır (döngüyü önler).
    public const string AnonymousClient = "hesap-anonymous";

    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    private readonly IHttpClientFactory _httpFactory;
    private readonly SessionStore _store;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthService(IHttpClientFactory httpFactory, SessionStore store)
    {
        _httpFactory = httpFactory;
        _store = store;
    }

    /// <summary>Oturum yenilenemediğinde (süre doldu, hesap kilitlendi, belirteç iptal edildi) tetiklenir.</summary>
    public event EventHandler? SessionExpired;

    public MobileSession? Session => _store.Current;

    private static string DeviceName =>
        $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString} • {DeviceInfo.Current.Model}";

    public async Task<ApiResult<MobileSession>> LoginAsync(string email, string password)
    {
        var result = await PostAsync<MobileSession>("api/auth/mobile/login", new { email, password, device = DeviceName });

        if (result.IsSuccess && result.Data != null)
            await SaveAsync(result.Data);

        return result;
    }

    public async Task<bool> TryRestoreAsync()
    {
        var session = await _store.LoadAsync();

        if (session == null)
            return false;

        if (session.RefreshExpiresAtUtc <= DateTime.UtcNow)
        {
            _store.Clear();
            return false;
        }

        return true;
    }

    /// <summary>Sunucudan gelen güncel kullanıcı bilgisini (abonelik durumu dahil) oturuma yazar.</summary>
    public async Task UpdateUserAsync(Me me)
    {
        if (_store.Current is { } s)
        {
            s.User = me;
            await _store.SaveAsync(s);
        }
    }

    /// <summary>
    /// Geçerli erişim belirtecini döner; süresi dolmak üzereyse önce yeniler.
    /// Eşzamanlı istekler tek bir yenileme çağrısını paylaşır.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var session = _store.Current;

        if (session == null)
            return null;

        if (!forceRefresh && session.AccessExpiresAtUtc > DateTime.UtcNow.AddSeconds(60))
            return session.AccessToken;

        var tokenBefore = session.AccessToken;

        await _refreshLock.WaitAsync(ct);

        try
        {
            // Bekleme sırasında başka bir istek zaten yenilediyse onu kullan.
            var current = _store.Current;

            if (current == null)
                return null;

            if (current.AccessToken != tokenBefore && current.AccessExpiresAtUtc > DateTime.UtcNow.AddSeconds(60))
                return current.AccessToken;

            var result = await PostAsync<MobileSession>("api/auth/mobile/refresh", new { refreshToken = current.RefreshToken });

            if (result.IsSuccess && result.Data != null)
            {
                await SaveAsync(result.Data);
                return result.Data.AccessToken;
            }

            // Ağ hatasında oturumu silme; yalnızca sunucu açıkça reddettiyse çıkış yap.
            if (result.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Locked)
            {
                _store.Clear();
                SessionExpired?.Invoke(this, EventArgs.Empty);
            }

            return null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task LogoutAsync()
    {
        var refreshToken = _store.Current?.RefreshToken;
        _store.Clear();

        if (string.IsNullOrEmpty(refreshToken))
            return;

        // Sunucuda yenileme belirteci iptal edilir; başarısız olursa yerel oturum yine de silinmiştir.
        await PostAsync<object>("api/auth/mobile/logout", new { refreshToken });
    }

    public Task<ApiResult<object>> ForgotPasswordAsync(string email) =>
        PostAsync<object>("api/auth/forgot-password", new { email });

    private Task SaveAsync(MobileSession s)
    {
        s.AccessExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, s.ExpiresIn));
        s.RefreshExpiresAtUtc = DateTime.UtcNow.Add(RefreshLifetime);
        return _store.SaveAsync(s);
    }

    private async Task<ApiResult<T>> PostAsync<T>(string path, object body)
    {
        try
        {
            var client = _httpFactory.CreateClient(AnonymousClient);
            using var response = await client.PostAsJsonAsync(path, body, ApiClient.Json);
            return await ApiClient.ReadAsync<T>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.FromException(ex);
        }
    }
}
