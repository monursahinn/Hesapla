using System.Reflection;

namespace Hesapla.Mobile.Config;

public static class AppConfig
{
    /// <summary>
    /// API kök adresi. Release: csproj'daki HesaplaApiBaseUrl (varsayılan https://hesapla.example.com/).
    /// Debug: yerel geliştirme sunucusu (Android emülatöründe bilgisayar 10.0.2.2 adresindedir).
    /// </summary>
    public static Uri ApiBaseUrl { get; } = ResolveBaseUrl();

    private static Uri ResolveBaseUrl()
    {
#if DEBUG
#if ANDROID
        return new Uri("http://10.0.2.2:5300/");
#else
        return new Uri("http://localhost:5300/");
#endif
#else
        var configured = typeof(AppConfig).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "HesaplaApiBaseUrl")?.Value;

        return new Uri(string.IsNullOrWhiteSpace(configured) ? "https://hesapla.example.com/" : configured);
#endif
    }
}
