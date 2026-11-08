using CommunityToolkit.Maui;
using Hesap.Mobile.Config;
using Hesap.Mobile.Services;
using Hesap.Mobile.ViewModels;
using Hesap.Mobile.Views.Pages;
using Microsoft.Extensions.Logging;

namespace Hesap.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Para, tarih ve sayı biçimleri cihaz dilinden bağımsız olarak Türkçe (1.234,50 ₺).
        var tr = new System.Globalization.CultureInfo("tr-TR");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = tr;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = tr;
        System.Globalization.CultureInfo.CurrentCulture = tr;
        System.Globalization.CultureInfo.CurrentUICulture = tr;

        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

#if ANDROID
        // Giriş alanları kendi çerçeveli kutularında (InputBorder) durur; Android'in alt çizgisi kaldırılır.
        static void NoUnderline(Android.Views.View v) =>
            v.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);

        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (h, _) => NoUnderline(h.PlatformView));
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("NoUnderline", (h, _) => NoUnderline(h.PlatformView));
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("NoUnderline", (h, _) => NoUnderline(h.PlatformView));
        Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping("NoUnderline", (h, _) => NoUnderline(h.PlatformView));
#endif

        // ---------------- HTTP ----------------
        builder.Services.AddSingleton<SessionStore>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddTransient<AuthHeaderHandler>();
        builder.Services.AddSingleton<ApiClient>();

        builder.Services.AddHttpClient(AuthService.AnonymousClient, ConfigureClient);
        builder.Services.AddHttpClient(ApiClient.AuthenticatedClient, ConfigureClient)
            .AddHttpMessageHandler<AuthHeaderHandler>();

        // ---------------- Sayfalar / ViewModel'ler ----------------
        builder.Services.AddSingleton<AppShell>();

        Register<LoginPage, LoginViewModel>(builder.Services);
        Register<ForgotPasswordPage, ForgotPasswordViewModel>(builder.Services);
        Register<DashboardPage, DashboardViewModel>(builder.Services);
        Register<CustomersPage, CustomersViewModel>(builder.Services);
        Register<CustomerDetailPage, CustomerDetailViewModel>(builder.Services);
        Register<InvoicesPage, InvoicesViewModel>(builder.Services);
        Register<InvoiceDetailPage, InvoiceDetailViewModel>(builder.Services);
        Register<CashPage, CashViewModel>(builder.Services);
        Register<MovementPage, MovementViewModel>(builder.Services);
        Register<SettingsPage, SettingsViewModel>(builder.Services);
        Register<ChangePasswordPage, ChangePasswordViewModel>(builder.Services);

        return builder.Build();
    }

    private static void ConfigureClient(HttpClient client)
    {
        client.BaseAddress = AppConfig.ApiBaseUrl;
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        // Sunucunun CSRF kuralı: belirteçsiz yazma istekleri (giriş, yenileme) bu başlığı taşımalı.
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
    }

    private static void Register<TPage, TViewModel>(IServiceCollection services)
        where TPage : Page
        where TViewModel : class
    {
        services.AddTransient<TPage>();
        services.AddTransient<TViewModel>();
    }
}
