using Poches.Core.Investments;

namespace Poches.Controls;

/// <summary>
/// Portfolio value (filled line), money invested (steps) and the benchmark bought with the same cash (line),
/// on one scale from zero.
/// </summary>
public sealed class EvolutionChartView : GraphicsView
{
    public static readonly BindableProperty PointsProperty =
        BindableProperty.Create(nameof(Points), typeof(IReadOnlyList<EvolutionPoint>), typeof(EvolutionChartView), null, propertyChanged: Redraw);

    public static readonly BindableProperty ValueColorProperty =
        BindableProperty.Create(nameof(ValueColor), typeof(Color), typeof(EvolutionChartView), Color.FromArgb("#4F46E5"), propertyChanged: Redraw);

    public static readonly BindableProperty BenchmarkColorProperty =
        BindableProperty.Create(nameof(BenchmarkColor), typeof(Color), typeof(EvolutionChartView), Color.FromArgb("#F97316"), propertyChanged: Redraw);

    public static readonly BindableProperty InvestedColorProperty =
        BindableProperty.Create(nameof(InvestedColor), typeof(Color), typeof(EvolutionChartView), Colors.Gray, propertyChanged: Redraw);

    public static readonly BindableProperty GridColorProperty =
        BindableProperty.Create(nameof(GridColor), typeof(Color), typeof(EvolutionChartView), Colors.LightGray, propertyChanged: Redraw);

    public static readonly BindableProperty LabelColorProperty =
        BindableProperty.Create(nameof(LabelColor), typeof(Color), typeof(EvolutionChartView), Colors.Gray, propertyChanged: Redraw);

    public static readonly BindableProperty LabelFormatterProperty =
        BindableProperty.Create(nameof(LabelFormatter), typeof(Func<double, string>), typeof(EvolutionChartView), null, propertyChanged: Redraw);

    public EvolutionChartView()
    {
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        Drawable = new EvolutionDrawable(this);
    }

    public IReadOnlyList<EvolutionPoint>? Points { get => (IReadOnlyList<EvolutionPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }

    public Color ValueColor { get => (Color)GetValue(ValueColorProperty); set => SetValue(ValueColorProperty, value); }

    public Color BenchmarkColor { get => (Color)GetValue(BenchmarkColorProperty); set => SetValue(BenchmarkColorProperty, value); }

    public Color InvestedColor { get => (Color)GetValue(InvestedColorProperty); set => SetValue(InvestedColorProperty, value); }

    public Color GridColor { get => (Color)GetValue(GridColorProperty); set => SetValue(GridColorProperty, value); }

    public Color LabelColor { get => (Color)GetValue(LabelColorProperty); set => SetValue(LabelColorProperty, value); }

    /// <summary>Formats the axis values ("6 k€"); plain numbers when not set.</summary>
    public Func<double, string>? LabelFormatter { get => (Func<double, string>?)GetValue(LabelFormatterProperty); set => SetValue(LabelFormatterProperty, value); }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) => ((EvolutionChartView)bindable).Invalidate();

    private sealed class EvolutionDrawable(EvolutionChartView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var points = owner.Points;
            if (points is null || points.Count < 2 || rect.Width <= 0)
                return;

            var max = points.Max(p => (double)Math.Max(Math.Max(p.Value, p.Invested), p.Benchmark ?? 0));
            var ticks = Ticks(max);
            var top = (float)ticks[^1];
            const float labelWidth = 46f, bottomPad = 4f, topPad = 6f;
            var plot = new RectF(rect.Left + labelWidth, rect.Top + topPad, rect.Width - labelWidth - 6, rect.Height - topPad - bottomPad);

            float X(int i) => plot.Left + plot.Width * i / (points.Count - 1);
            float Y(double v) => plot.Bottom - plot.Height * (float)(v / top);

            // Grid and labels.
            canvas.FontSize = 10.5f;
            canvas.FontColor = owner.LabelColor;
            canvas.StrokeSize = 1;
            canvas.StrokeColor = owner.GridColor;
            foreach (var tick in ticks)
            {
                var y = Y(tick);
                canvas.DrawLine(plot.Left, y, plot.Right, y);
                var label = owner.LabelFormatter?.Invoke(tick) ?? tick.ToString("0");
                canvas.DrawString(label, rect.Left, y - 8, labelWidth - 6, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            // Value: area wash and line.
            var area = new PathF();
            area.MoveTo(X(0), Y((double)points[0].Value));
            for (var i = 1; i < points.Count; i++) area.LineTo(X(i), Y((double)points[i].Value));
            var line = new PathF(area);
            area.LineTo(X(points.Count - 1), plot.Bottom);
            area.LineTo(X(0), plot.Bottom);
            area.Close();
            canvas.SetFillPaint(new LinearGradientPaint(
                [new PaintGradientStop(0, owner.ValueColor.WithAlpha(0.28f)), new PaintGradientStop(1, owner.ValueColor.WithAlpha(0.02f))],
                new Point(0, 0), new Point(0, 1)), plot);
            canvas.FillPath(area);

            // Invested: steps.
            var steps = new PathF();
            steps.MoveTo(X(0), Y((double)points[0].Invested));
            for (var i = 1; i < points.Count; i++)
            {
                steps.LineTo(X(i), Y((double)points[i - 1].Invested));
                steps.LineTo(X(i), Y((double)points[i].Invested));
            }
            canvas.StrokeColor = owner.InvestedColor;
            canvas.StrokeSize = 1.5f;
            canvas.StrokeDashPattern = null;
            canvas.DrawPath(steps);

            // Benchmark.
            if (points.Any(p => p.Benchmark is not null))
            {
                var bench = new PathF();
                var started = false;
                for (var i = 0; i < points.Count; i++)
                {
                    if (points[i].Benchmark is not { } b) continue;
                    if (!started) { bench.MoveTo(X(i), Y((double)b)); started = true; }
                    else bench.LineTo(X(i), Y((double)b));
                }
                Stroke(canvas, owner.BenchmarkColor);
                canvas.DrawPath(bench);
                if (points[^1].Benchmark is { } lastBench)
                    Dot(canvas, X(points.Count - 1), Y((double)lastBench), owner.BenchmarkColor);
            }

            Stroke(canvas, owner.ValueColor);
            canvas.DrawPath(line);
            Dot(canvas, X(points.Count - 1), Y((double)points[^1].Value), owner.ValueColor);
        }

        private static void Stroke(ICanvas canvas, Color color)
        {
            canvas.StrokeColor = color;
            canvas.StrokeSize = 2.2f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
        }

        private static void Dot(ICanvas canvas, float x, float y, Color color)
        {
            canvas.FillColor = color;
            canvas.FillCircle(x, y, 4);
        }

        /// <summary>Round steps from zero up to just above <paramref name="max"/> (0, 2 500, 5 000, 7 500).</summary>
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
