using CommunityToolkit.Mvvm.ComponentModel;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>Icon and colour chosen for a pocket or a subscription, with the grids to pick them from.</summary>
public sealed partial class AppearanceSelection : ObservableObject
{
    private const int GridColumns = 6;

    public AppearanceSelection(IReadOnlyList<string> emojis)
    {
        IconOptions = SelectableOption.Grid(emojis, GridColumns, SelectIcon);
        ColorOptions = SelectableOption.Grid(Palette.Colors, GridColumns, SelectColor);
        SelectIcon(IconOptions[0]);
        SelectColor(ColorOptions[0]);
    }

    public IReadOnlyList<SelectableOption> IconOptions { get; }

    public IReadOnlyList<SelectableOption> ColorOptions { get; }

    public string ColorHex { get; private set; } = Palette.Colors[0];

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private Color _color = Colors.Transparent;

    [ObservableProperty]
    private Color _softColor = Colors.Transparent;

    [ObservableProperty]
    private Brush _brush = Brush.Transparent;

    /// <summary>Shows an existing item's icon and colour, even when they are not part of the grids.</summary>
    public void Show(string icon, string colorHex)
    {
        SelectIcon(IconOptions.FirstOrDefault(o => o.Value == icon) ?? new SelectableOption(icon, 0, 1, _ => { }));
        SelectColor(ColorOptions.FirstOrDefault(o => o.Value.Equals(colorHex, StringComparison.OrdinalIgnoreCase))
            ?? new SelectableOption(colorHex, 0, 1, _ => { }));
    }

    private void SelectIcon(SelectableOption option)
    {
        Icon = option.Value;
        Select(IconOptions, option.Value);
    }

    private void SelectColor(SelectableOption option)
    {
        ColorHex = option.Value;
        Color = option.Color;
        SoftColor = Palette.Soft(option.Color);
        Brush = Palette.HeroBrush(option.Color);
        Select(ColorOptions, option.Value);
        foreach (var icon in IconOptions)
        {
            icon.Accent = Color;
            icon.AccentSoft = SoftColor;
        }
    }

    private static void Select(IEnumerable<SelectableOption> options, string value)
    {
        foreach (var option in options)
            option.IsSelected = string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase);
    }
}
