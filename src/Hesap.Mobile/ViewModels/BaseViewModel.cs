using Hesap.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hesap.Mobile.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(ShowSkeleton))]
    public partial bool IsBusy { get; set; }

    /// <summary>Sayfa verisi en az bir kez yüklendi mi (iskelet yalnızca ilk yüklemede görünür).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSkeleton))]
    public partial bool HasLoaded { get; set; }

    public bool ShowSkeleton => IsBusy && !HasLoaded && !IsRefreshing;

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>Sayfa her göründüğünde çalıştırılacak yükleme komutu (yoksa null).</summary>
    public virtual IAsyncRelayCommand? AutoLoadCommand => null;

    public virtual void OnDisappearing()
    {
    }

    public bool IsNotBusy => !IsBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>Yükleme göstergesi + hata yakalama ile işlemi çalıştırır (çift tıklamayı engeller).</summary>
    protected async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ErrorMessage = ApiResult<object>.FromException(ex).Message;
        }
        finally
        {
            HasLoaded = true;
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    /// <summary>API sonucunu değerlendirir; başarısızsa hata mesajını gösterir.</summary>
    protected bool Check<T>(ApiResult<T> result)
    {
        if (result.IsSuccess)
            return true;

        ErrorMessage = result.Message;
        return false;
    }

    /// <summary>
    /// Kısa bilgi çubuğu (alttan, temaya uygun renkte; 3,5 sn). Android'in düz Toast'u yerine kullanılır.
    /// </summary>
    protected static Task ToastAsync(string message, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Task.CompletedTask;

        static Color R(string key, string fallback) => ThemeService.Resource(key, fallback);

        var options = new CommunityToolkit.Maui.Core.SnackbarOptions
        {
            BackgroundColor = ThemeService.IsDark ? R("CardAltBackgroundColor", "#101826") : Color.FromArgb("#1F2937"),
            TextColor = Colors.White,
            ActionButtonTextColor = isError ? R("ErrorTextColor", "#F87171") : R("AccentTeal", "#2DD4BF"),
            CornerRadius = new CornerRadius(12),
            Font = Microsoft.Maui.Font.SystemFontOfSize(14),
            ActionButtonFont = Microsoft.Maui.Font.SystemFontOfSize(14, FontWeight.Bold),
        };

        return CommunityToolkit.Maui.Alerts.Snackbar
            .Make(message, action: null, actionButtonText: "Tamam", duration: TimeSpan.FromSeconds(3.5), visualOptions: options)
            .Show();
    }

    protected static Task AlertAsync(string title, string message) =>
        Shell.Current.DisplayAlertAsync(title, message, "Tamam");

    protected static Task<bool> ConfirmAsync(string title, string message, string accept) =>
        Shell.Current.DisplayAlertAsync(title, message, accept, "Vazgeç");
}
