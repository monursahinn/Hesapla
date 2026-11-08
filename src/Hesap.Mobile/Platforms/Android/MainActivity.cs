using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Hesap.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Tema uygulama nesnesi oluşurken (pencere hazır olmadan) uygulandığı için çubuk simgeleri burada ayarlanır.
            Services.ThemeService.UpdateSystemBars();
        }
    }
}
