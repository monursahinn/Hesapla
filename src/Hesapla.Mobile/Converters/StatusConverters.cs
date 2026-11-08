using Hesapla.Mobile.Services;
using System.Globalization;

namespace Hesapla.Mobile.Converters;

/// <summary>Sunucudaki fatura durum kodlarının mobil karşılıkları.</summary>
public static class StatusCatalog
{
    public enum Tone { Neutral, Info, Warning, Success, Danger }

    private static readonly Dictionary<string, Dictionary<string, (string Text, Tone Tone)>> Map = new()
    {
        ["invoice"] = new()
        {
            ["BE"] = ("Bekliyor", Tone.Warning),
            ["KO"] = ("Kısmi ödendi", Tone.Info),
            ["OD"] = ("Ödendi", Tone.Success),
            ["GE"] = ("Vadesi geçti", Tone.Danger),
            ["IP"] = ("İptal", Tone.Neutral)
        }
    };

    public static (string Text, Tone Tone) Get(string kind, string? code)
    {
        var key = (code ?? "").Trim().ToUpperInvariant();

        if (Map.TryGetValue(kind, out var m))
        {
            if (m.TryGetValue(key, out var v))
                return v;

            // Bazı uç noktalar kod yerine metin döndürür (ör. arıza: "İşleme Alındı").
            var byText = m.Values.FirstOrDefault(x =>
                string.Equals(x.Text, code?.Trim(), StringComparison.CurrentCultureIgnoreCase));

            if (byText.Text != null)
                return byText;
        }

        return (string.IsNullOrWhiteSpace(code) ? "-" : code, Tone.Neutral);
    }

    // Renkler temadan okunur (açık temalarda daha koyu tonlar; bkz. Services/ThemeService).
    public static Color ToColor(Tone tone) => tone switch
    {
        Tone.Success => ThemeService.Resource("SuccessTextColor", "#4ADE80"),
        Tone.Warning => ThemeService.Resource("WarningTextColor", "#FBBF24"),
        Tone.Danger => ThemeService.Resource("ErrorTextColor", "#F87171"),
        Tone.Info => ThemeService.Resource("InfoTextColor", "#60A5FA"),
        _ => ThemeService.Resource("NeutralTextColor", "#9CA3AF")
    };
}

/// <summary>Kod -> Türkçe metin. ConverterParameter: invoice</summary>
public sealed class StatusTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        StatusCatalog.Get(parameter as string ?? "", value as string).Text;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Kod -> renk. ConverterParameter: invoice</summary>
public sealed class StatusColorConverter : IValueConverter
{
    /// <summary>1 = metin rengi; ~0.15 = rozet zemini (aynı rengin açık tonu).</summary>
    public float Alpha { get; set; } = 1f;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = StatusCatalog.ToColor(StatusCatalog.Get(parameter as string ?? "", value as string).Tone).WithAlpha(Alpha);

        // Border.Stroke bir Brush bekler; Label.TextColor bir Color.
        return targetType == typeof(Brush) ? new SolidColorBrush(color) : color;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

public sealed class HasTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Para birimi: 1.234,50 ₺</summary>
public sealed class CurrencyConverter : IValueConverter
{
    private static readonly CultureInfo Tr = new("tr-TR");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal d ? d.ToString("N2", Tr) + " ₺" : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Tarihi yerel saate çevirip biçimler. ConverterParameter: biçim (varsayılan dd.MM.yyyy HH:mm)</summary>
public sealed class DateTextConverter : IValueConverter
{
    private static readonly CultureInfo Tr = new("tr-TR");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter as string ?? "dd.MM.yyyy HH:mm";

        return value switch
        {
            DateTime dt when dt == default => "-",
            DateTime dt => (dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt).ToString(format, Tr),
            _ => "-"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Sayı &gt; 0 ise true; ConverterParameter=invert ile tersi (boş liste mesajı için).</summary>
public sealed class IntToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is int i && i > 0) != (parameter as string == "invert");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>[kod, tür] -> Türkçe metin (tür satıra göre değişen listelerde).</summary>
public sealed class StatusTextMultiConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        StatusCatalog.Get(values.ElementAtOrDefault(1) as string ?? "", values.ElementAtOrDefault(0) as string).Text;

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>[kod, tür] -> renk.</summary>
public sealed class StatusColorMultiConverter : IMultiValueConverter
{
    public float Alpha { get; set; } = 1f;

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = StatusCatalog.ToColor(
            StatusCatalog.Get(values.ElementAtOrDefault(1) as string ?? "", values.ElementAtOrDefault(0) as string).Tone).WithAlpha(Alpha);

        // Border.Stroke bir Brush bekler; Label.TextColor bir Color.
        return targetType == typeof(Brush) ? new SolidColorBrush(color) : color;
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value != null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
