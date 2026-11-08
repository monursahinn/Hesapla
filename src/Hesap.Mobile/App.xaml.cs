namespace Hesap.Mobile;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    // AppShell constructor'a enjekte edilmez: Shell XAML'ı uygulama kaynaklarına (renk/stil) ihtiyaç duyar,
    // bu yüzden InitializeComponent'ten sonra, pencere oluşturulurken çözülür.
    public App(IServiceProvider services)
    {
        InitializeComponent();

        // Son kullanılan tema (giriş ekranı dahil).
        Services.ThemeService.ApplyStored(this);

        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<AppShell>()) { Title = "Hesap" };
}
