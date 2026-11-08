using System.Text.Json;
using Hesapla.Mobile.Models.Api;

namespace Hesapla.Mobile.Services;

/// <summary>
/// Oturum bilgisini cihazın güvenli deposunda tutar (Android Keystore / iOS Keychain).
/// Token'lar asla Preferences'a veya log'a yazılmaz.
/// </summary>
public sealed class SessionStore
{
    private const string Key = "hesapla.session.v1";

    private MobileSession? _current;

    public MobileSession? Current => _current;

    public bool IsAuthenticated => _current != null && !string.IsNullOrEmpty(_current.RefreshToken);

    public async Task<MobileSession?> LoadAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(Key);
            _current = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<MobileSession>(json);
        }
        catch
        {
            // Anahtar deposu bozulmuş olabilir (ör. yedekten geri yükleme): temiz oturumla başla.
            SecureStorage.Default.Remove(Key);
            _current = null;
        }

        return _current;
    }

    public async Task SaveAsync(MobileSession session)
    {
        _current = session;
        await SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(session));
    }

    public void Clear()
    {
        _current = null;
        SecureStorage.Default.Remove(Key);
    }
}
