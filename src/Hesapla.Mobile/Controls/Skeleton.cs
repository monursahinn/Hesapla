using Microsoft.Maui.Controls.Shapes;

namespace Hesapla.Mobile.Controls;

/// <summary>
/// İlk yüklemede içerik iskeleti: kart biçiminde yer tutucular hafifçe "nefes alır". Boş ekranda dönen
/// simge yerine verinin geleceği yeri gösterir. Animasyon yalnızca görünürken çalışır.
/// </summary>
public sealed class Skeleton : ContentView
{
    public static readonly BindableProperty RowsProperty =
        BindableProperty.Create(nameof(Rows), typeof(int), typeof(Skeleton), 4, propertyChanged: (b, _, _) => ((Skeleton)b).Build());

    private readonly VerticalStackLayout _stack = new() { Spacing = 10 };
    private bool _animating;

    public Skeleton()
    {
        Content = _stack;
        Build();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
                ToggleAnimation();
        };
        Loaded += (_, _) => ToggleAnimation();
        Unloaded += (_, _) => StopAnimation();
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    private void Build()
    {
        _stack.Children.Clear();
        for (var i = 0; i < Rows; i++)
        {
            var card = new Border
            {
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                Padding = new Thickness(14, 14),
                Content = new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        Bar(i % 2 == 0 ? 0.55 : 0.7, 14),
                        Bar(0.4, 10),
                        Bar(0.85, 10),
                    }
                }
            };
            card.SetDynamicResource(Border.BackgroundColorProperty, "CardBackgroundColor");
            card.SetDynamicResource(Border.StrokeProperty, "CardBorderColor");
            _stack.Children.Add(card);
        }
    }

    private static BoxView Bar(double widthRatio, double height)
    {
        var bar = new BoxView { HeightRequest = height, CornerRadius = 5, HorizontalOptions = LayoutOptions.Start };
        bar.SetDynamicResource(BoxView.ColorProperty, "SeparatorColor");
        bar.WidthRequest = 300 * widthRatio;
        return bar;
    }

    private void ToggleAnimation()
    {
        if (IsVisible && Handler != null)
            StartAnimation();
        else
            StopAnimation();
    }

    private void StartAnimation()
    {
        if (_animating)
            return;
        _animating = true;
        var pulse = new Animation
        {
            { 0, 0.5, new Animation(v => _stack.Opacity = v, 1, 0.45) },
            { 0.5, 1, new Animation(v => _stack.Opacity = v, 0.45, 1) },
        };
        pulse.Commit(this, "skeleton-pulse", length: 1200, repeat: () => _animating);
    }

    private void StopAnimation()
    {
        _animating = false;
        this.AbortAnimation("skeleton-pulse");
        _stack.Opacity = 1;
    }
}
