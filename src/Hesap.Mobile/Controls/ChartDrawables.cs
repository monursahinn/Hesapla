using Hesap.Mobile.Services;

namespace Hesap.Mobile.Controls;

/// <summary>
/// Dashboard grafikleri için hafif, yerel (GraphicsView) çizimler.
/// Önceki sürüm her grafik için ayrı bir WebView + CDN'den Chart.js yüklüyordu.
/// </summary>
public static class ChartPalette
{
    public static readonly Color[] Colors =
    {
        Color.FromArgb("#2DD4BF"), Color.FromArgb("#3B82F6"), Color.FromArgb("#FBBF24"),
        Color.FromArgb("#F87171"), Color.FromArgb("#A78BFA"), Color.FromArgb("#4ADE80"),
        Color.FromArgb("#F472B6"), Color.FromArgb("#22D3EE")
    };

    public static Color At(int i) => Colors[i % Colors.Length];

    // Metin / ızgara renkleri temadan okunur (çizim her yenilendiğinde güncel tema).
    public static Color Text => ThemeService.Resource("SecondaryTextColor", "#A1A1AA");
    public static Color Grid => ThemeService.Resource("SeparatorColor", "#1F2937");
    public static Color Strong => ThemeService.Resource("PrimaryTextColor", "#FFFFFF");
}

public sealed record ChartPoint(string Label, double Value);

public sealed class DonutChartDrawable : IDrawable
{
    public IReadOnlyList<ChartPoint> Points { get; set; } = Array.Empty<ChartPoint>();

    public void Draw(ICanvas canvas, RectF rect)
    {
        var total = Points.Sum(p => Math.Max(0, p.Value));
        var legendHeight = Points.Count * 20f;
        var size = Math.Min(rect.Width, rect.Height - legendHeight - 10);

        if (size <= 20)
            return;

        var cx = rect.Center.X;
        var cy = rect.Top + size / 2 + 4;
        var r = size / 2 - 6;
        var thickness = r * 0.38f;

        canvas.StrokeLineCap = LineCap.Butt;
        canvas.StrokeSize = thickness;

        if (total <= 0)
        {
            canvas.StrokeColor = ChartPalette.Grid;
            canvas.DrawEllipse(cx - r + thickness / 2, cy - r + thickness / 2, 2 * r - thickness, 2 * r - thickness);
        }
        else
        {
            float start = 90;

            for (var i = 0; i < Points.Count; i++)
            {
                var sweep = (float)(Math.Max(0, Points[i].Value) / total * 360);

                if (sweep <= 0)
                    continue;

                canvas.StrokeColor = ChartPalette.At(i);

                // Tam tur (tek dilim) açılarda başlangıç = bitiş olur ve bazı platformlar hiç çizmez.
                if (sweep >= 359.5f)
                {
                    canvas.DrawEllipse(cx - r + thickness / 2, cy - r + thickness / 2, 2 * r - thickness, 2 * r - thickness);
                    break;
                }

                canvas.DrawArc(cx - r + thickness / 2, cy - r + thickness / 2, 2 * r - thickness, 2 * r - thickness,
                    start, start - sweep, clockwise: true, closed: false);
                start -= sweep;
            }
        }

        canvas.FontColor = ChartPalette.Strong;
        canvas.FontSize = 18;
        canvas.DrawString(total.ToString("N0"), cx - 60, cy - 12, 120, 24, HorizontalAlignment.Center, VerticalAlignment.Center);

        var y = rect.Top + size + 14;
        canvas.FontSize = 12;

        for (var i = 0; i < Points.Count; i++)
        {
            canvas.FillColor = ChartPalette.At(i);
            canvas.FillRoundedRectangle(rect.Left + 8, y + 4, 10, 10, 3);
            canvas.FontColor = ChartPalette.Text;
            canvas.DrawString($"{Points[i].Label}  ({Points[i].Value:N0})", rect.Left + 26, y, rect.Width - 30, 18,
                HorizontalAlignment.Left, VerticalAlignment.Center);
            y += 20;
        }
    }
}

public sealed class BarChartDrawable : IDrawable
{
    public IReadOnlyList<ChartPoint> Points { get; set; } = Array.Empty<ChartPoint>();

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (Points.Count == 0)
            return;

        var max = Math.Max(1, Points.Max(p => p.Value));
        var labelWidth = Math.Min(130f, rect.Width * 0.38f);
        var rowHeight = Math.Min(30f, rect.Height / Points.Count);
        var barArea = rect.Width - labelWidth - 50;

        canvas.FontSize = 12;

        for (var i = 0; i < Points.Count; i++)
        {
            var y = rect.Top + i * rowHeight;
            var w = (float)(Points[i].Value / max * barArea);

            canvas.FontColor = ChartPalette.Text;
            canvas.DrawString(Points[i].Label, rect.Left, y, labelWidth - 6, rowHeight,
                HorizontalAlignment.Left, VerticalAlignment.Center);

            canvas.FillColor = ChartPalette.Grid;
            canvas.FillRoundedRectangle(rect.Left + labelWidth, y + rowHeight * 0.25f, barArea, rowHeight * 0.5f, 4);

            canvas.FillColor = ChartPalette.At(i);
            canvas.FillRoundedRectangle(rect.Left + labelWidth, y + rowHeight * 0.25f, Math.Max(2, w), rowHeight * 0.5f, 4);

            canvas.FontColor = ChartPalette.Strong;
            canvas.DrawString(Points[i].Value.ToString("N0"), rect.Left + labelWidth + barArea + 4, y, 46, rowHeight,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }
}

public sealed class LineChartDrawable : IDrawable
{
    public IReadOnlyList<ChartPoint> Points { get; set; } = Array.Empty<ChartPoint>();
    public Color LineColor { get; set; } = ChartPalette.At(0);

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (Points.Count == 0)
            return;

        const float bottomPad = 20, leftPad = 4;
        var plot = new RectF(rect.Left + leftPad, rect.Top + 8, rect.Width - leftPad - 4, rect.Height - bottomPad - 8);
        var max = Math.Max(1, Points.Max(p => p.Value));

        canvas.StrokeColor = ChartPalette.Grid;
        canvas.StrokeSize = 1;

        for (var g = 0; g <= 3; g++)
        {
            var gy = plot.Top + plot.Height * g / 3f;
            canvas.DrawLine(plot.Left, gy, plot.Right, gy);
        }

        var step = Points.Count > 1 ? plot.Width / (Points.Count - 1) : 0;
        var path = new PathF();

        for (var i = 0; i < Points.Count; i++)
        {
            var x = plot.Left + i * step;
            var y = plot.Bottom - (float)(Points[i].Value / max * plot.Height);

            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }

        canvas.StrokeColor = LineColor;
        canvas.StrokeSize = 2.5f;
        canvas.DrawPath(path);

        canvas.FontColor = ChartPalette.Text;
        canvas.FontSize = 10;
        var labelEvery = Math.Max(1, Points.Count / 5);

        for (var i = 0; i < Points.Count; i += labelEvery)
        {
            canvas.DrawString(Points[i].Label, plot.Left + i * step - 24, plot.Bottom + 4, 48, 14,
                HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        canvas.DrawString(max.ToString("N0"), plot.Left + 2, plot.Top - 2, 80, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
    }
}
