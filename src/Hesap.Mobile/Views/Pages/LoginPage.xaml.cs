using Hesap.Mobile.ViewModels;

namespace Hesap.Mobile.Views.Pages;

public partial class LoginPage : BasePage
{
    public LoginPage(LoginViewModel viewModel) : base(viewModel)
    {
        InitializeComponent();

        // Uygulama açılırken oturum geri yükleniyorsa form yerine açılış görünümü gösterilir.
        RestoreOverlay.IsVisible = AppShell.IsRestoring;
        AppShell.RestoreFinished += OnRestoreFinished;
    }

    private void OnRestoreFinished() =>
        MainThread.BeginInvokeOnMainThread(() => RestoreOverlay.IsVisible = false);

    // Giriş ekranında "geri" tuşu uygulamayı kapatmalı, korumalı sayfalara dönmemeli.
    protected override bool OnBackButtonPressed() => false;
}
