namespace Hesap.Mobile.Services;

/// <summary>
/// Kullanıcının tema tercihi (web ile ortak: Dark, Light, DarkBlue, Turquoise, Sand, Gray).
/// Renkler Colors.xaml'daki anahtarların yerine uygulama kaynaklarına yazılır; sayfalar renkleri
/// DynamicResource ile kullandığı için değişiklik açık ekranlara da anında yansır.
/// Durum çubuğu her temada marka rengindedir (bkz. UpdateSystemBars).
/// </summary>
public static class ThemeService
{
    private const string PrefKey = "hesap.theme";

    public sealed record ThemeOption(string Value, string Label, bool IsDark, Color Swatch, Color Accent);

    private static readonly Dictionary<string, string> DarkStatus = new()
    {
        ["SuccessBgColor"] = "#0D2A1F", ["SuccessBorderColor"] = "#22C55E", ["SuccessTextColor"] = "#4ADE80",
        ["WarningTextColor"] = "#FBBF24", ["WarningBgColor"] = "#2A2110",
        ["ErrorTextColor"] = "#F87171", ["ErrorBgColor"] = "#2A1215",
        ["InfoTextColor"] = "#60A5FA", ["InfoBgColor"] = "#0F1D33",
        ["NeutralTextColor"] = "#9CA3AF",
    };

    private static readonly Dictionary<string, string> LightStatus = new()
    {
        ["SuccessBgColor"] = "#DCFCE7", ["SuccessBorderColor"] = "#16A34A", ["SuccessTextColor"] = "#15803D",
        ["WarningTextColor"] = "#B45309", ["WarningBgColor"] = "#FEF3C7",
        ["ErrorTextColor"] = "#DC2626", ["ErrorBgColor"] = "#FEE2E2",
        ["InfoTextColor"] = "#1D4ED8", ["InfoBgColor"] = "#DBEAFE",
        ["NeutralTextColor"] = "#6B7280",
    };

    // Anahtar sırası: sayfa, ayraç, üst çubuk, kart, kart kenarı, ikincil kart, metin (ana/ikincil/soluk),
    // giriş alanı (zemin/metin/ipucu/kenar/odak), vurgu, ikinci vurgu, vurgu üstü metin, kendi mesaj balonu.
    private static readonly Dictionary<string, (bool dark, string[] c)> Palettes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dark"] = (true, new[]
        {
            "#050B16", "#1F2937", "#0A1220", "#151A24", "#2B3445", "#101826", "#FFFFFF", "#A1A1AA", "#9CA3AF",
            "#0B1220", "#F8FAFC", "#6B7280", "#2F3B50", "#818CF8", "#818CF8", "#3B82F6", "#0B0A24", "#1E1B4B",
        }),
        ["Light"] = (false, new[]
        {
            "#F4F6F9", "#E2E6EC", "#FFFFFF", "#FFFFFF", "#E2E6EC", "#F8FAFC", "#111827", "#4B5563", "#6B7280",
            "#FFFFFF", "#111827", "#9CA3AF", "#D1D5DB", "#4F46E5", "#4F46E5", "#2563EB", "#FFFFFF", "#E0E7FF",
        }),
        ["DarkBlue"] = (true, new[]
        {
            "#0C132D", "#2C3A6B", "#121A3A", "#172146", "#2C3A6B", "#121A3A", "#FFFFFF", "#BCC6E3", "#93A1CC",
            "#101836", "#F8FAFC", "#6677AD", "#3A4A80", "#4DABF7", "#4DABF7", "#748FFC", "#0A1028", "#1F3A6B",
        }),
        ["Turquoise"] = (false, new[]
        {
            "#E3F8FB", "#B9E6EE", "#F2FCFD", "#FFFFFF", "#B9E6EE", "#F2FCFD", "#0B2730", "#35535C", "#5F7A82",
            "#FFFFFF", "#0B2730", "#8AA3AA", "#A5DCE6", "#0C8599", "#0C8599", "#1971C2", "#FFFFFF", "#C5F1F7",
        }),
        ["Sand"] = (false, new[]
        {
            "#F3EEE3", "#E2D9C6", "#FAF6EE", "#FFFDF8", "#E2D9C6", "#FAF6EE", "#2B2418", "#5C5140", "#857A66",
            "#FFFDF8", "#2B2418", "#A89D88", "#D9CDB4", "#A0742A", "#A0742A", "#8A6322", "#FFFFFF", "#F2E8D6",
        }),
        ["Gray"] = (false, new[]
        {
            "#E7EAEE", "#CFD5DC", "#F1F3F5", "#F8F9FA", "#CFD5DC", "#F1F3F5", "#1F2328", "#495057", "#6C757D",
            "#FFFFFF", "#1F2328", "#9AA1A9", "#C3CAD2", "#495057", "#495057", "#343A40", "#FFFFFF", "#DEE2E6",
        }),
    };

    private static readonly string[] Keys =
    {
        "PageBackgroundColor", "SeparatorColor", "HeaderBackgroundColor", "CardBackgroundColor", "CardBorderColor",
        "CardAltBackgroundColor", "PrimaryTextColor", "SecondaryTextColor", "MutedTextColor",
        "InputBackgroundColor", "InputTextColor", "InputPlaceholderColor", "InputBorderColor", "InputFocusedBorderColor",
        "AccentTeal", "AccentBlue", "OnAccentColor", "ChatBubbleMineColor",
    };

    public static IReadOnlyList<ThemeOption> Options { get; } = new[]
    {
        Option("Dark", "Koyu"), Option("Light", "Açık"), Option("DarkBlue", "Lacivert"),
        Option("Turquoise", "Turkuaz"), Option("Sand", "Kum"), Option("Gray", "Gri"),
    };

    private static ThemeOption Option(string value, string label)
    {
        var (dark, c) = Palettes[value];
        return new ThemeOption(value, label, dark, Color.FromArgb(c[0]), Color.FromArgb(c[14]));
    }

    /// <summary>Uygulanan tema (bilinmeyen değerler koyuya düşer).</summary>
    public static string Current { get; private set; } = "Dark";

    public static bool IsDark => Palettes[Current].dark;

    public static event EventHandler? Changed;

    public static string Normalize(string? theme) =>
        Palettes.Keys.FirstOrDefault(k => k.Equals(theme?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Dark";

    /// <summary>Uygulama açılışında son kullanılan temayı (giriş öncesi de) uygular.</summary>
    public static void ApplyStored(Application app) => Apply(Preferences.Default.Get(PrefKey, "Light"), app);

    public static void Apply(string? theme, Application? app = null)
    {
        var name = Normalize(theme);
        app ??= Application.Current;
        if (app == null)
            return;

        var (dark, colors) = Palettes[name];
        var res = app.Resources;

        for (var i = 0; i < Keys.Length; i++)
            res[Keys[i]] = Color.FromArgb(colors[i]);

        foreach (var (k, v) in dark ? DarkStatus : LightStatus)
            res[k] = Color.FromArgb(v);

        res["PrimaryGradient"] = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(colors[14]), 0f),
                new GradientStop(Color.FromArgb(colors[15]), 1f),
            },
            new Point(0, 0), new Point(1, 0));

        app.UserAppTheme = dark ? AppTheme.Dark : AppTheme.Light;
        Current = name;
        Preferences.Default.Set(PrefKey, name);

        UpdateSystemBars();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Durum çubuğu her temada marka çivit mavisi + açık simgeler (Shell gezinmede çubuğu colorPrimary ile boyar;
    /// sabit renk her ekranda okunaklı kalır). Gezinme çubuğu simgeleri temaya uyar.
    /// </summary>
    public static void UpdateSystemBars()
    {
#if ANDROID
        var window = Platform.CurrentActivity?.Window;
        if (window == null)
            return;

#pragma warning disable CA1422 // Android 15+ uçtan uca çizimde renk çağrısı yok sayılır; simge ayarı yine geçerlidir.
        window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#4F46E5"));
#pragma warning restore CA1422

        var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
        if (controller == null)
            return;

        controller.AppearanceLightStatusBars = false;
        controller.AppearanceLightNavigationBars = !IsDark;
#endif
    }

    /// <summary>Kaynaklardan renk okur (kod tarafındaki çizimler / dönüştürücüler için).</summary>
    public static Color Resource(string key, string fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Color.FromArgb(fallback);
}
