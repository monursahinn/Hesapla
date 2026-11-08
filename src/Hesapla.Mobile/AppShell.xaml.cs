using Hesapla.Mobile.Models.Api;
using Hesapla.Mobile.Services;
using Hesapla.Mobile.Views.Pages;

namespace Hesapla.Mobile;

public partial class AppShell : Shell
{
    private readonly AuthService _auth;
    private readonly ApiClient _api;
    private bool _started;

    public AppShell(AuthService auth, ApiClient api)
    {
        InitializeComponent();

        _auth = auth;
        _api = api;
        ApplyFlyoutBackground();
        ThemeService.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(ApplyFlyoutBackground);
        BindingContext = this;

        // Menüde olmayan alt sayfalar
        Routing.RegisterRoute("forgot-password", typeof(ForgotPasswordPage));
        Routing.RegisterRoute("customer", typeof(CustomerDetailPage));
        Routing.RegisterRoute("invoice", typeof(InvoiceDetailPage));
        Routing.RegisterRoute("movement", typeof(MovementPage));
        Routing.RegisterRoute("change-password", typeof(ChangePasswordPage));

        _auth.SessionExpired += (_, _) => MainThread.BeginInvokeOnMainThread(async () =>
        {
            await GoToLoginAsync();
            await DisplayAlertAsync("Oturum sona erdi", "Güvenliğiniz için lütfen tekrar giriş yapın.", "Tamam");
        });
    }

    /// <summary>Açılışta kayıtlı oturum geri yükleniyor (giriş sayfası bu sırada açılış görünümü gösterir).</summary>
    public static bool IsRestoring { get; private set; } = true;

    public static event Action? RestoreFinished;

    /// <summary>Oturumdaki kullanıcı (abonelik ve yetki bilgisi dahil).</summary>
    public Me? User => _auth.Session?.User;

    public string UserFullName { get; private set; } = "";
    public string UserRoleText { get; private set; } = "";
    public string FirmName { get; private set; } = "";

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_started)
            return;

        _started = true;

        try
        {
            if (await _auth.TryRestoreAsync() && await RefreshUserAsync())
                await GoToAsync("//dashboard");
            else
                await GoToLoginAsync();
        }
        finally
        {
            IsRestoring = false;
            RestoreFinished?.Invoke();
        }
    }

    /// <summary>
    /// Girişten sonra ve açılışta çağrılır: kullanıcı/firma/abonelik bilgisini sunucudan tazeler.
    /// Ağ yoksa son bilinen oturumla devam edilir; sunucu reddettiyse false döner.
    /// </summary>
    public async Task<bool> RefreshUserAsync()
    {
        var me = await _api.GetMeAsync();

        if (me.IsSuccess && me.Data != null)
        {
            if (me.Data.Firm == null)
                return false; // Platform yöneticisi: mobil uygulama firma kullanıcıları içindir.

            await _auth.UpdateUserAsync(me.Data);
        }
        else if (me.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            return false;
        }

        ApplyUser();
        return _auth.Session != null;
    }

    private void ApplyUser()
    {
        var u = _auth.Session?.User;
        UserFullName = u?.FullName ?? "";
        UserRoleText = u?.RoleLabel ?? "";
        FirmName = u?.Firm?.Name ?? "";
        OnPropertyChanged(nameof(UserFullName));
        OnPropertyChanged(nameof(UserRoleText));
        OnPropertyChanged(nameof(FirmName));
    }

    /// <summary>Menü zemini: üstte marka çivit mavisi, altta temanın üst çubuk rengi.</summary>
    private void ApplyFlyoutBackground()
    {
        var brand = Color.FromArgb("#312E81");
        var body = ThemeService.Resource("HeaderBackgroundColor", "#FFFFFF");
        FlyoutBackground = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(brand, 0f),
                new GradientStop(brand, 0.12f),
                new GradientStop(body, 0.12f),
                new GradientStop(body, 1f),
            },
            new Point(0, 0), new Point(0, 1));
    }

    public async Task GoToLoginAsync()
    {
        FlyoutIsPresented = false;
        await GoToAsync("//login");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync("Çıkış", "Oturumu kapatmak istiyor musunuz?", "Çıkış yap", "Vazgeç"))
            return;

        await _auth.LogoutAsync();
        await GoToLoginAsync();
    }
}
