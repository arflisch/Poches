using System.ComponentModel;

namespace Poches.Controls;

/// <summary>
/// The two blocks of a page: one above the other on a phone, side by side once the page is wide enough
/// (iPad, large Mac window). When the second block is hidden (nothing to list yet), the first one takes the
/// whole width.
/// </summary>
public sealed class TwoPane : Grid
{
    /// <summary>Below this width, two columns would be narrower than a phone screen.</summary>
    public const double WideWidth = 820;

    public static readonly BindableProperty StartProperty =
        BindableProperty.Create(nameof(Start), typeof(View), typeof(TwoPane), null, propertyChanged: OnPaneChanged);

    public static readonly BindableProperty EndProperty =
        BindableProperty.Create(nameof(End), typeof(View), typeof(TwoPane), null, propertyChanged: OnPaneChanged);

    /// <summary>Space between the two blocks, vertical or horizontal depending on the layout.</summary>
    public static readonly BindableProperty SpacingProperty =
        BindableProperty.Create(nameof(Spacing), typeof(double), typeof(TwoPane), 22.0, propertyChanged: (b, _, _) => ((TwoPane)b).Arrange());

    private bool _isWide;

    public TwoPane()
    {
        RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];
        ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
        Arrange();
    }

    public View? Start
    {
        get => (View?)GetValue(StartProperty);
        set => SetValue(StartProperty, value);
    }

    public View? End
    {
        get => (View?)GetValue(EndProperty);
        set => SetValue(EndProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Whether the blocks are currently side by side.</summary>
    public bool IsWide => _isWide;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateMode();
    }

    private static void OnPaneChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var pane = (TwoPane)bindable;
        if (oldValue is View old)
        {
            old.PropertyChanged -= pane.OnPanePropertyChanged;
            pane.Children.Remove(old);
        }
        if (newValue is View view)
        {
            view.PropertyChanged += pane.OnPanePropertyChanged;
            view.VerticalOptions = LayoutOptions.Start;
            pane.Children.Add(view);
        }
        pane.UpdateMode(force: true);
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsVisible))
            UpdateMode(force: true);
    }

    private void UpdateMode(bool force = false)
    {
        var wide = Width >= WideWidth && End?.IsVisible == true;
        if (wide == _isWide && !force)
            return;
        _isWide = wide;
        // Changing the grid while it is being laid out would be ignored: do it right after.
        Dispatcher.Dispatch(Arrange);
    }

    private void Arrange()
    {
        ColumnDefinitions = _isWide
            ? [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)]
            : [new ColumnDefinition(GridLength.Star)];
        ColumnSpacing = _isWide ? Spacing : 0;
        RowSpacing = _isWide || End?.IsVisible != true || Start?.IsVisible != true ? 0 : Spacing;

        if (Start is { } start)
        {
            start.SetValue(RowProperty, 0);
            start.SetValue(ColumnProperty, 0);
        }
        if (End is { } end)
        {
            end.SetValue(RowProperty, _isWide ? 0 : 1);
            end.SetValue(ColumnProperty, _isWide ? 1 : 0);
        }
    }
}
