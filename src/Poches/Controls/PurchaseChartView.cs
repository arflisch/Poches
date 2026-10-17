namespace Poches.Controls;

/// <param name="Gain">Whether this purchase is worth more than it cost.</param>
public sealed record PurchaseDot(DateTime Date, double Price, double Invested, bool Gain);

/// <summary>What <see cref="PurchaseChartView"/> draws: each purchase at the price paid, today's price and the average cost.</summary>
public sealed record PurchaseChartData(IReadOnlyList<PurchaseDot> Purchases, double Current, double AverageCost, DateTime End);

/// <summary>
/// Price paid at each purchase (a dot sized by the amount invested) against today's price and the average cost:
/// how far each dot sits below or above the line is that purchase's gain or loss.
/// </summary>
public sealed class PurchaseChartView : GraphicsView
{
    public static readonly BindableProperty DataProperty =
        BindableProperty.Create(nameof(Data), typeof(PurchaseChartData), typeof(PurchaseChartView), null, propertyChanged: Redraw);

    public static readonly BindableProperty CurrentColorProperty =
        BindableProperty.Create(nameof(CurrentColor), typeof(Color), typeof(PurchaseChartView), Color.FromArgb("#4F46E5"), propertyChanged: Redraw);

    public static readonly BindableProperty GainColorProperty =
        BindableProperty.Create(nameof(GainColor), typeof(Color), typeof(PurchaseChartView), Color.FromArgb("#059669"), propertyChanged: Redraw);

    public static readonly BindableProperty LossColorProperty =
        BindableProperty.Create(nameof(LossColor), typeof(Color), typeof(PurchaseChartView), Color.FromArgb("#E11D48"), propertyChanged: Redraw);

    public static readonly BindableProperty GridColorProperty =
        BindableProperty.Create(nameof(GridColor), typeof(Color), typeof(PurchaseChartView), Colors.LightGray, propertyChanged: Redraw);

    public static readonly BindableProperty LabelColorProperty =
        BindableProperty.Create(nameof(LabelColor), typeof(Color), typeof(PurchaseChartView), Colors.Gray, propertyChanged: Redraw);

    public static readonly BindableProperty SurfaceColorProperty =
        BindableProperty.Create(nameof(SurfaceColor), typeof(Color), typeof(PurchaseChartView), Colors.White, propertyChanged: Redraw);

    public static readonly BindableProperty LabelFormatterProperty =
        BindableProperty.Create(nameof(LabelFormatter), typeof(Func<double, string>), typeof(PurchaseChartView), null, propertyChanged: Redraw);

    public PurchaseChartView()
    {
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        Drawable = new PurchaseDrawable(this);
    }

    public PurchaseChartData? Data { get => (PurchaseChartData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }

    public Color CurrentColor { get => (Color)GetValue(CurrentColorProperty); set => SetValue(CurrentColorProperty, value); }

    public Color GainColor { get => (Color)GetValue(GainColorProperty); set => SetValue(GainColorProperty, value); }

    public Color LossColor { get => (Color)GetValue(LossColorProperty); set => SetValue(LossColorProperty, value); }

    public Color GridColor { get => (Color)GetValue(GridColorProperty); set => SetValue(GridColorProperty, value); }

    public Color LabelColor { get => (Color)GetValue(LabelColorProperty); set => SetValue(LabelColorProperty, value); }

    /// <summary>Background behind the chart, used to ring the dots so they stay distinct where they overlap.</summary>
    public Color SurfaceColor { get => (Color)GetValue(SurfaceColorProperty); set => SetValue(SurfaceColorProperty, value); }

    public Func<double, string>? LabelFormatter { get => (Func<double, string>?)GetValue(LabelFormatterProperty); set => SetValue(LabelFormatterProperty, value); }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) => ((PurchaseChartView)bindable).Invalidate();

    private sealed class PurchaseDrawable(PurchaseChartView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var data = owner.Data;
            if (data is null || data.Purchases.Count == 0 || rect.Width <= 0)
                return;

            var prices = data.Purchases.Select(p => p.Price).Append(data.Current).Append(data.AverageCost).ToList();
            var min = prices.Min();
            var max = prices.Max();
            var span = max - min;
            if (span < max * 0.02) span = Math.Max(max * 0.1, 1);
            min -= span * 0.2;
            max += span * 0.2;

            const float labelWidth = 50f;
            var plot = new RectF(rect.Left + labelWidth, rect.Top + 18, rect.Width - labelWidth - 10, rect.Height - 30);
            var start = data.Purchases.Min(p => p.Date).AddDays(-10);
            var end = (data.End > data.Purchases.Max(p => p.Date) ? data.End : data.Purchases.Max(p => p.Date)).AddDays(10);
            var days = Math.Max(1, (end - start).TotalDays);
            float X(DateTime d) => plot.Left + plot.Width * (float)((d - start).TotalDays / days);
            float Y(double v) => plot.Bottom - plot.Height * (float)((v - min) / (max - min));

            canvas.FontSize = 10.5f;
            canvas.FontColor = owner.LabelColor;
            canvas.StrokeColor = owner.GridColor;
            canvas.StrokeSize = 1;
            for (var i = 0; i <= 3; i++)
            {
                var v = min + (max - min) * i / 3;
                var y = Y(v);
                canvas.DrawLine(plot.Left, y, plot.Right, y);
                canvas.DrawString(owner.LabelFormatter?.Invoke(v) ?? v.ToString("0.##"), rect.Left, y - 8, labelWidth - 6, 16,
                    HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            var current = Y(data.Current);
            var average = Y(data.AverageCost);
            canvas.StrokeColor = owner.LabelColor;
            canvas.StrokeSize = 1;
            canvas.DrawLine(plot.Left, average, plot.Right, average);
            canvas.StrokeColor = owner.CurrentColor;
            canvas.StrokeSize = 2.2f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawLine(plot.Left, current, plot.Right, current);

            var biggest = data.Purchases.Max(p => p.Invested);
            foreach (var purchase in data.Purchases.OrderByDescending(p => p.Invested))
            {
                var x = X(purchase.Date);
                var y = Y(purchase.Price);
                var color = purchase.Gain ? owner.GainColor : owner.LossColor;
                canvas.StrokeColor = color.WithAlpha(0.35f);
                canvas.StrokeSize = 2;
                canvas.DrawLine(x, y, x, current);
                var radius = 4.5f + 6.5f * (float)Math.Sqrt(biggest > 0 ? purchase.Invested / biggest : 1);
                canvas.FillColor = owner.SurfaceColor;
                canvas.FillCircle(x, y, radius + 2);
                canvas.FillColor = color;
                canvas.FillCircle(x, y, radius);
            }
        }
    }
}
