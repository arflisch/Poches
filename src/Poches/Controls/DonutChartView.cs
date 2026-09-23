namespace Poches.Controls;

public sealed record ChartSlice(double Value, Color Color);

/// <summary>Animated donut chart with rounded, slightly separated segments.</summary>
public sealed class DonutChartView : GraphicsView
{
    public static readonly BindableProperty SlicesProperty =
        BindableProperty.Create(nameof(Slices), typeof(IReadOnlyList<ChartSlice>), typeof(DonutChartView), null,
            propertyChanged: (b, o, n) => ((DonutChartView)b).OnSlicesChanged(o as IReadOnlyList<ChartSlice>));

    public static readonly BindableProperty ThicknessProperty =
        BindableProperty.Create(nameof(Thickness), typeof(double), typeof(DonutChartView), 18.0,
            propertyChanged: (b, _, _) => ((DonutChartView)b).Invalidate());

    public static readonly BindableProperty TrackColorProperty =
        BindableProperty.Create(nameof(TrackColor), typeof(Color), typeof(DonutChartView), Colors.LightGray,
            propertyChanged: (b, _, _) => ((DonutChartView)b).Invalidate());

    private float _progress = 1f;
    private bool _animationPending;

    public DonutChartView()
    {
        BackgroundColor = Colors.Transparent;
        Drawable = new DonutDrawable(this);
    }

    public IReadOnlyList<ChartSlice>? Slices
    {
        get => (IReadOnlyList<ChartSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is not null && _animationPending)
            Reveal();
    }

    private void OnSlicesChanged(IReadOnlyList<ChartSlice>? previous)
    {
        // Only play the reveal animation when data first appears; later updates just redraw.
        if (previous is null || previous.Count == 0)
        {
            _progress = 0f;
            if (Handler is null)
                _animationPending = true;
            else
                Reveal();
        }
        Invalidate();
    }

    private void Reveal()
    {
        _animationPending = false;
        this.AbortAnimation("reveal");
        new Animation(v => { _progress = (float)v; Invalidate(); }, 0, 1, Easing.CubicOut)
            .Commit(this, "reveal", length: 900);
    }

    private sealed class DonutDrawable(DonutChartView owner) : IDrawable
    {
        private const double VisibleGapDegrees = 3;

        public void Draw(ICanvas canvas, RectF rect)
        {
            var thickness = (float)owner.Thickness;
            var radius = (Math.Min(rect.Width, rect.Height) - thickness) / 2f;
            if (radius <= 0)
                return;
            var center = rect.Center;

            canvas.StrokeSize = thickness;
            canvas.StrokeColor = owner.TrackColor;
            canvas.DrawCircle(center, radius);

            var slices = owner.Slices?.Where(s => s.Value > 0).ToList();
            if (slices is null || slices.Count == 0)
                return;

            var total = slices.Sum(s => s.Value);
            var revealEnd = -90 + 360 * owner._progress;
            canvas.StrokeLineCap = LineCap.Round;

            if (slices.Count == 1)
            {
                canvas.StrokeColor = slices[0].Color;
                if (owner._progress >= 1f)
                    canvas.DrawCircle(center, radius);
                else
                    canvas.DrawPath(Arc(center, radius, -90, revealEnd));
                return;
            }

            // Round caps extend each arc by half the thickness, so widen the gap accordingly.
            var capDegrees = thickness / 2f / radius * 180 / Math.PI;
            var gap = VisibleGapDegrees + 2 * capDegrees;
            var start = -90d;
            foreach (var (slice, sweep) in slices.Zip(ComputeSweeps(slices, gap)))
            {
                var from = start + gap / 2;
                var to = Math.Max(from + 0.01, start + sweep - gap / 2); // tiny slices become a dot
                start += sweep;

                to = Math.Min(to, revealEnd);
                if (to <= from)
                    continue;
                canvas.StrokeColor = slice.Color;
                canvas.DrawPath(Arc(center, radius, from, to));
            }
        }

        /// <summary>Gives every slice at least enough room for a visible dot, shrinking the larger ones.</summary>
        private static IReadOnlyList<double> ComputeSweeps(IReadOnlyList<ChartSlice> slices, double gap)
        {
            var total = slices.Sum(s => s.Value);
            var raw = slices.Select(s => s.Value / total * 360).ToList();
            var minimum = gap + VisibleGapDegrees;
            var smallCount = raw.Count(r => r < minimum);
            if (smallCount == 0 || smallCount * minimum >= 360)
                return raw;

            var largeTotal = raw.Where(r => r >= minimum).Sum();
            var scale = (360 - smallCount * minimum) / largeTotal;
            return raw.Select(r => r < minimum ? minimum : r * scale).ToList();
        }

        private static PathF Arc(PointF center, float radius, double fromDegrees, double toDegrees)
        {
            var path = new PathF();
            var steps = Math.Max(2, (int)Math.Ceiling((toDegrees - fromDegrees) / 2));
            for (var i = 0; i <= steps; i++)
            {
                var angle = (fromDegrees + (toDegrees - fromDegrees) * i / steps) * Math.PI / 180;
                var point = new PointF(center.X + radius * (float)Math.Cos(angle), center.Y + radius * (float)Math.Sin(angle));
                if (i == 0)
                    path.MoveTo(point);
                else
                    path.LineTo(point);
            }
            return path;
        }
    }
}
