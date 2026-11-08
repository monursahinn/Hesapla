using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hesap.Mobile.Config;
using Hesap.Mobile.Services;

namespace Hesap.Mobile.ViewModels;

public sealed partial class LoginViewModel : BaseViewModel
{
    private readonly AuthService _auth;

    public LoginViewModel(AuthService auth)
    {
        _auth = auth;
    }

    [ObservableProperty]
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPasswordHidden { get; set; } = true;

    [RelayCommand]
    private void TogglePassword() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private Task LoginAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "E-posta ve şifre zorunludur.";
            return;
        }

        var result = await _auth.LoginAsync(Email.Trim(), Password);

        if (!Check(result))
            return;

        var shell = (AppShell)Shell.Current;

        if (!await shell.RefreshUserAsync())
        {
            await _auth.LogoutAsync();
            ErrorMessage = "Mobil uygulama firma kullanıcıları içindir. Platform yönetimi web panelinden yapılır.";
            return;
        }

        Password = "";
        await Shell.Current.GoToAsync("//dashboard");
    });

    [RelayCommand]
    private Task ForgotPasswordAsync() =>
        Shell.Current.GoToAsync("forgot-password", new Dictionary<string, object> { ["email"] = Email.Trim() });

    /// <summary>Yeni firma kaydı web panelinden yapılır (14 gün ücretsiz deneme).</summary>
    [RelayCommand]
    private Task RegisterAsync() => Launcher.Default.OpenAsync(new Uri(AppConfig.ApiBaseUrl, "register"));
}

public sealed partial class ForgotPasswordViewModel : BaseViewModel, IQueryAttributable
{
    private readonly AuthService _auth;

    public ForgotPasswordViewModel(AuthService auth)
    {
        _auth = auth;
    }

    [ObservableProperty]
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("email", out var e) && e is string s && string.IsNullOrEmpty(Email))
            Email = s;
    }

    [RelayCommand]
    private Task SendAsync() => RunAsync(async () =>
    {
        SuccessMessage = null;

        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
        {
            ErrorMessage = "Geçerli bir e-posta adresi girin.";
            return;
        }

        var result = await _auth.ForgotPasswordAsync(Email.Trim());

        if (Check(result))
            SuccessMessage = string.IsNullOrWhiteSpace(result.Message)
                ? "Bu e-posta adresi kayıtlıysa şifre sıfırlama bağlantısı gönderildi."
                : result.Message;
    });
}

public sealed partial class ChangePasswordViewModel : BaseViewModel
{
    private readonly ApiClient _api;

    public ChangePasswordViewModel(ApiClient api)
    {
        _api = api;
    }

    [ObservableProperty]
    public partial string CurrentPassword { get; set; } = "";

    [ObservableProperty]
    public partial string NewPassword { get; set; } = "";

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = "";

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrEmpty(CurrentPassword) || string.IsNullOrEmpty(NewPassword))
        {
            ErrorMessage = "Mevcut ve yeni şifre zorunludur.";
            return;
        }

        // Sunucu kuralı: en az 8 karakter, en az bir harf ve bir rakam.
        if (NewPassword.Length < 8 || !NewPassword.Any(char.IsLetter) || !NewPassword.Any(char.IsDigit))
        {
            ErrorMessage = "Yeni şifre en az 8 karakter olmalı, harf ve rakam içermelidir.";
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "Yeni şifreler eşleşmiyor.";
            return;
        }

        var result = await _api.ChangePasswordAsync(CurrentPassword, NewPassword);

        if (!Check(result))
            return;

        CurrentPassword = NewPassword = ConfirmPassword = "";
        await ToastAsync("Şifreniz değiştirildi.");
        await Shell.Current.GoToAsync("..");
    });
}
