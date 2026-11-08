using System.Globalization;
using Microsoft.Maui.Graphics;

namespace Hesap.Mobile.Controls;

/// <summary>
/// Çizgi simge (Tabler Icons çizim dili, 24×24 ızgara). Web arayüzündeki simgelerle aynı görünür;
/// yazı tipi gerektirmez ve tema rengini alır. Kullanım: &lt;controls:Icon Name="bell" Size="20" Color="{DynamicResource AccentTeal}" /&gt;
/// </summary>
public sealed class Icon : ContentView
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = "M5 4h4a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1M5 16h4a1 1 0 0 1 1 1v2a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-2a1 1 0 0 1 1-1M15 12h4a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1M15 4h4a1 1 0 0 1 1 1v2a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1",
        ["search"] = "M3 10a7 7 0 1 0 14 0a7 7 0 1 0-14 0M21 21l-6-6",
        ["receipt"] = "M5 21V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v16l-3-2-2 2-2-2-2 2-2-2-3 2M14 8h-2.5a1.5 1.5 0 0 0 0 3h1a1.5 1.5 0 0 1 0 3H10M12 14v1.5M12 6.5V8",
        ["scale"] = "M7 20h10M6 6l6-1 6 1M12 3v17M9 12l-3-6-3 6a3 3 0 0 0 6 0M21 12l-3-6-3 6a3 3 0 0 0 6 0",
        ["speaker"] = "M18 8a3 3 0 0 1 0 6M10 8v11a1 1 0 0 1-1 1H8a1 1 0 0 1-1-1v-5M12 8l4.6-3.8A0.9 0.9 0 0 1 18 5v12a0.9 0.9 0 0 1-1.4 0.8L12 14H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1h8",
        ["tool"] = "M7 10h3V7L6.5 3.5a6 6 0 0 1 8 8l6 6a2 2 0 0 1-3 3l-6-6a6 6 0 0 1-8-8L7 10",
        ["package"] = "M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3M12 12l8-4.5M12 12v9M12 12L4 7.5M16 5.25l-8 4.5",
        ["message"] = "M8 9h8M8 13h6M18 4a3 3 0 0 1 3 3v8a3 3 0 0 1-3 3h-5l-5 3v-3H6a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3h12",
        ["bell"] = "M10 5a2 2 0 1 1 4 0a7 7 0 0 1 4 6v3a4 4 0 0 0 2 3H4a4 4 0 0 0 2-3v-3a7 7 0 0 1 4-6M9 17v1a3 3 0 0 0 6 0v-1",
        ["user"] = "M8 7a4 4 0 1 0 8 0a4 4 0 1 0-8 0M6 21v-2a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v2",
        ["users"] = "M5 7a4 4 0 1 0 8 0a4 4 0 1 0-8 0M3 21v-2a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v2M16 3.13a4 4 0 0 1 0 7.75M21 21v-2a4 4 0 0 0-3-3.85",
        ["home"] = "M5 12H3l9-9 9 9h-2M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-7M9 21v-6a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v6",
        ["cash"] = "M7 9h12a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-6a2 2 0 0 1 2-2M11 14a2 2 0 1 0 4 0a2 2 0 1 0-4 0M17 9V7a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2",
        ["alert"] = "M12 9v4M10.4 3.9L2.2 17.6A1.9 1.9 0 0 0 3.8 20.4h16.4a1.9 1.9 0 0 0 1.6-2.8L13.6 3.9a1.9 1.9 0 0 0-3.2 0M12 16h0.01",
        ["key"] = "M16.6 3.4a4.9 4.9 0 0 1 0 7l-1.2 1.2a4.9 4.9 0 0 1-5.5 1L5 17.4V20H2v-3l5.1-5.1a4.9 4.9 0 0 1 1-5.5l1.2-1.2a4.9 4.9 0 0 1 7 0M15 9h0.01",
        ["lock"] = "M5 13a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-6M8 11V7a4 4 0 1 1 8 0v4",
        ["logout"] = "M14 8V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h7a2 2 0 0 0 2-2v-2M9 12h12l-3-3M18 15l3-3",
        ["chevron"] = "M9 6l6 6-6 6",
        ["check"] = "M5 12l5 5L20 7",
        ["plus"] = "M12 5v14M5 12h14",
        ["clock"] = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0M12 7v5l3 3",
        ["calendar"] = "M4 7a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V7M16 3v4M8 3v4M4 11h16",
        ["shield"] = "M12 3a12 12 0 0 0 8.5 3A12 12 0 0 1 12 21A12 12 0 0 1 3.5 6A12 12 0 0 0 12 3M9 12l2 2 4-4",
        ["chart"] = "M3 12h4l3 8 4-16 3 8h4",
        ["inbox"] = "M4 6a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6M4 13h3l3 3h4l3-3h3",
        ["send"] = "M10 14L21 3M21 3l-6.5 18a0.55 0.55 0 0 1-1 0L10 14l-7-3.5a0.55 0.55 0 0 1 0-1L21 3",
        ["edit"] = "M7 7H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2v-1M20.4 6.6a2.1 2.1 0 0 0-3-3L9 12v3h3l8.4-8.4M16 5l3 3",
        ["eye"] = "M10 12a2 2 0 1 0 4 0a2 2 0 1 0-4 0M21 12c-2.4 4-5.4 6-9 6s-6.6-2-9-6c2.4-4 5.4-6 9-6s6.6 2 9 6",
        ["eye-off"] = "M10.6 10.6a2 2 0 0 0 2.8 2.8M16.7 16.7A8.7 8.7 0 0 1 12 18c-3.6 0-6.6-2-9-6c1.3-2.1 2.7-3.7 4.3-4.7M9.9 6.2A9 9 0 0 1 12 6c3.6 0 6.6 2 9 6c-0.7 1.1-1.4 2.1-2.2 3M3 3l18 18",
        ["mail-in"] = "M3 7a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7M3 7l9 6 9-6",
        ["door"] = "M14 12v0.01M3 21h18M6 21V5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v16",
    };

    public static readonly BindableProperty NameProperty =
        BindableProperty.Create(nameof(Name), typeof(string), typeof(Icon), "", propertyChanged: (b, _, _) => ((Icon)b).Rebuild());

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(Icon), 22d, propertyChanged: (b, _, _) => ((Icon)b).Rebuild());

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(Icon), null, propertyChanged: (b, _, _) => ((Icon)b).Rebuild());

    // SVG yolu doğrudan tuval üzerine çizilir (24×24 ızgara ölçeklenir); Shapes.Path geometriyi kendi
    // sınırlarına kaydırdığı için küçük boyutlarda simgeler kırpılıyordu.
    private readonly IconDrawable _drawable = new();
    private readonly GraphicsView _view;

    public Icon()
    {
        InputTransparent = true;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        _view = new GraphicsView { Drawable = _drawable, InputTransparent = true, BackgroundColor = Colors.Transparent };
        Content = _view;
        Rebuild();
    }

    public string Name
    {
        get => (string)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Color? Color
    {
        get => (Color?)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public static bool Exists(string? name) => name != null && Paths.ContainsKey(name);

    private void Rebuild()
    {
        WidthRequest = HeightRequest = Size;
        _view.WidthRequest = _view.HeightRequest = Size;
        _drawable.Path = Paths.TryGetValue(Name ?? "", out var d) ? PathBuilder.Build(d) : null;
        _drawable.Color = Color ?? (Application.Current?.Resources.TryGetValue("PrimaryTextColor", out var v) == true && v is Color pc ? pc : Colors.Gray);
        _view.Invalidate();
    }

    private sealed class IconDrawable : IDrawable
    {
        public PathF? Path { get; set; }
        public Color Color { get; set; } = Colors.Gray;

        public void Draw(ICanvas canvas, RectF rect)
        {
            if (Path == null)
                return;

            var scale = Math.Min(rect.Width, rect.Height) / 24f;
            canvas.SaveState();
            canvas.Translate(rect.X, rect.Y);
            canvas.Scale(scale, scale);
            canvas.StrokeColor = Color;
            canvas.StrokeSize = 1.8f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(Path);
            canvas.RestoreState();
        }
    }
}

/// <summary>Kabuk menü rotası → simge adı.</summary>
public sealed class RouteToIconConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = "dashboard",
        ["customers"] = "users",
        ["invoices"] = "receipt",
        ["cash"] = "cash",
        ["settings"] = "user",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var route = (value as string ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        return Map.FirstOrDefault(kv => route.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)).Value ?? "chevron";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Sunucunun panel özetinde döndürdüğü emoji → simge adı (bilinmeyenler için grafik simgesi).</summary>
public sealed class EmojiToIconConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Map = new()
    {
        ["💳"] = "receipt", ["💰"] = "cash", ["🏠"] = "home", ["🏢"] = "home", ["🛠"] = "tool", ["🔧"] = "tool",
        ["🧑"] = "users", ["👥"] = "users", ["🚶"] = "users", ["📦"] = "package", ["📢"] = "speaker", ["📣"] = "speaker",
        ["⏳"] = "clock", ["📮"] = "inbox", ["📝"] = "edit", ["📄"] = "receipt", ["🕒"] = "clock", ["⚠"] = "alert", ["🔔"] = "bell", ["💬"] = "message", ["🧾"] = "scale",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        if (Icon.Exists(s))
            return s;
        return Map.FirstOrDefault(kv => s.Contains(kv.Key, StringComparison.Ordinal)).Value ?? "chart";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
