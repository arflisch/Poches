using Poches.Core.Investments;

namespace Poches.Controls;

/// <summary>
/// Simulated value month by month: the band between the 10th and 90th percentiles, the median line, and the goal
/// as a dashed line, on one scale from zero.
/// </summary>
public sealed class ProjectionChartView : GraphicsView
{
    public static readonly BindableProperty BandsProperty =
        BindableProperty.Create(nameof(Bands), typeof(IReadOnlyList<ProjectionBand>), typeof(ProjectionChartView), null, propertyChanged: Redraw);

    public static readonly BindableProperty GoalProperty =
        BindableProperty.Create(nameof(Goal), typeof(double), typeof(ProjectionChartView), 0d, propertyChanged: Redraw);

    public static readonly BindableProperty LineColorProperty =
        BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(ProjectionChartView), Color.FromArgb("#4F46E5"), propertyChanged: Redraw);

    public static readonly BindableProperty GoalColorProperty =
        BindableProperty.Create(nameof(GoalColor), typeof(Color), typeof(ProjectionChartView), Colors.Gray, propertyChanged: Redraw);

    public static readonly BindableProperty GridColorProperty =
        BindableProperty.Create(nameof(GridColor), typeof(Color), typeof(ProjectionChartView), Colors.LightGray, propertyChanged: Redraw);

    public static readonly BindableProperty LabelColorProperty =
        BindableProperty.Create(nameof(LabelColor), typeof(Color), typeof(ProjectionChartView), Colors.Gray, propertyChanged: Redraw);

    public static readonly BindableProperty LabelFormatterProperty =
        BindableProperty.Create(nameof(LabelFormatter), typeof(Func<double, string>), typeof(ProjectionChartView), null, propertyChanged: Redraw);

    public ProjectionChartView()
    {
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        Drawable = new ProjectionDrawable(this);
    }

    public IReadOnlyList<ProjectionBand>? Bands { get => (IReadOnlyList<ProjectionBand>?)GetValue(BandsProperty); set => SetValue(BandsProperty, value); }

    /// <summary>The goal amount; 0 for none.</summary>
    public double Goal { get => (double)GetValue(GoalProperty); set => SetValue(GoalProperty, value); }

    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    public Color GoalColor { get => (Color)GetValue(GoalColorProperty); set => SetValue(GoalColorProperty, value); }

    public Color GridColor { get => (Color)GetValue(GridColorProperty); set => SetValue(GridColorProperty, value); }

    public Color LabelColor { get => (Color)GetValue(LabelColorProperty); set => SetValue(LabelColorProperty, value); }

    public Func<double, string>? LabelFormatter { get => (Func<double, string>?)GetValue(LabelFormatterProperty); set => SetValue(LabelFormatterProperty, value); }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) => ((ProjectionChartView)bindable).Invalidate();

    private sealed class ProjectionDrawable(ProjectionChartView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var bands = owner.Bands;
            if (bands is null || bands.Count < 2 || rect.Width <= 0)
                return;

            var goal = owner.Goal;
            var max = Math.Max(bands.Max(b => b.P90), goal);
            var ticks = Ticks(max);
            var top = (float)ticks[^1];
            const float bottomPad = 18f, topPad = 6f;
            canvas.FontSize = 10.5f;
            var labels = ticks.Select(t => owner.LabelFormatter?.Invoke(t) ?? t.ToString("0")).ToList();
            var labelWidth = Math.Clamp(labels.Max(l => canvas.GetStringSize(l, Microsoft.Maui.Graphics.Font.Default, 10.5f).Width) * 1.25f + 12f, 40f, rect.Width / 3);
            var plot = new RectF(rect.Left + labelWidth, rect.Top + topPad, rect.Width - labelWidth - 6, rect.Height - topPad - bottomPad);
            var months = bands[^1].Month;

            float X(int month) => plot.Left + plot.Width * month / months;
            float Y(double v) => plot.Bottom - plot.Height * (float)(Math.Max(0, v) / top);

            canvas.FontColor = owner.LabelColor;
            canvas.StrokeSize = 1;
            canvas.StrokeColor = owner.GridColor;
            for (var i = 0; i < ticks.Count; i++)
            {
                var y = Y(ticks[i]);
                canvas.DrawLine(plot.Left, y, plot.Right, y);
                canvas.DrawString(labels[i], rect.Left, y - 8, labelWidth - 6, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            // Years along the bottom: "+5 y" style marks every year or every few years.
            var years = months / 12;
            var everyYears = years <= 6 ? 1 : years <= 15 ? 5 : 10;
            for (var y = everyYears; y * 12 <= months; y += everyYears)
                canvas.DrawString($"+{y}", X(y * 12) - 20, plot.Bottom + 2, 40, 14, HorizontalAlignment.Center, VerticalAlignment.Top);

            // The band between the pessimistic and optimistic paths.
            var band = new PathF();
            band.MoveTo(X(bands[0].Month), Y(bands[0].P90));
            foreach (var b in bands.Skip(1)) band.LineTo(X(b.Month), Y(b.P90));
            for (var i = bands.Count - 1; i >= 0; i--) band.LineTo(X(bands[i].Month), Y(bands[i].P10));
            band.Close();
            canvas.FillColor = owner.LineColor.WithAlpha(0.16f);
            canvas.FillPath(band);

            if (goal > 0)
            {
                canvas.StrokeColor = owner.GoalColor;
                canvas.StrokeSize = 1.5f;
                canvas.StrokeDashPattern = [4, 4];
                canvas.DrawLine(plot.Left, Y(goal), plot.Right, Y(goal));
                canvas.StrokeDashPattern = null;
            }

            var median = new PathF();
            median.MoveTo(X(bands[0].Month), Y(bands[0].P50));
            foreach (var b in bands.Skip(1)) median.LineTo(X(b.Month), Y(b.P50));
            canvas.StrokeColor = owner.LineColor;
            canvas.StrokeSize = 2.2f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(median);
            canvas.FillColor = owner.LineColor;
            canvas.FillCircle(X(bands[^1].Month), Y(bands[^1].P50), 4);
        }

        private static IReadOnlyList<double> Ticks(double max)
        {
            if (max <= 0) return [0, 1];
            var raw = max / 3;
            var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            var step = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * magnitude).First(s => s >= raw);
            var ticks = new List<double>();
            for (var v = 0.0; v < max + step * 0.999; v += step) ticks.Add(v);
            return ticks;
        }
    }
}
