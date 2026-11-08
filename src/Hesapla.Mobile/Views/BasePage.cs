using Hesapla.Mobile.ViewModels;

namespace Hesapla.Mobile.Views;

/// <summary>Tüm sayfaların tabanı: ViewModel'i bağlar, sayfa her göründüğünde veriyi yeniler.</summary>
public abstract class BasePage : ContentPage
{
    protected BasePage(BaseViewModel viewModel)
    {
        BindingContext = viewModel;
    }

    protected BaseViewModel ViewModel => (BaseViewModel)BindingContext;

    protected override void OnAppearing()
    {
        base.OnAppearing();

        var load = ViewModel.AutoLoadCommand;

        if (load != null && load.CanExecute(null))
            load.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ViewModel.OnDisappearing();
    }
}
