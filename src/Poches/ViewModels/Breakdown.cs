using System.ComponentModel;
using Poches.Controls;
using Poches.Core.Formatting;
using Poches.Localization;

namespace Poches.ViewModels;

public sealed record LegendItem(string Name, Color Color, string Percent);

/// <summary>What the shared breakdown card (donut chart and legend) displays.</summary>
public interface IBreakdown : INotifyPropertyChanged
{
    IReadOnlyList<ChartSlice> ChartSlices { get; }

    IReadOnlyList<LegendItem> Legend { get; }

    string ChartCenterValue { get; }

    string ChartCenterLabel { get; }
}

public static class Breakdown
{
    /// <summary>Beyond this many items, the smallest ones are grouped as "Others".</summary>
    private const int MaxSlices = 5;

    private static readonly Color OthersColor = Color.FromArgb("#94A3B8");

    /// <summary>Donut slices and legend lines for the positive values, largest first.</summary>
    public static (IReadOnlyList<ChartSlice> Slices, IReadOnlyList<LegendItem> Legend) Build(
        IEnumerable<(string Name, Color Color, decimal Value)> items, MoneyFormatter formatter)
    {
        var funded = items.Where(i => i.Value > 0).OrderByDescending(i => i.Value).ToList();
        var total = funded.Sum(i => i.Value);
        string Percent(decimal value) => formatter.FormatPercent(total > 0 ? (double)(value / total) : 0);

        var shown = funded.Count > MaxSlices ? funded.Take(MaxSlices - 1).ToList() : funded;
        var others = funded.Skip(shown.Count).ToList();

        var slices = shown.Select(i => new ChartSlice((double)i.Value, i.Color)).ToList();
        var legend = shown.Select(i => new LegendItem(i.Name, i.Color, Percent(i.Value))).ToList();
        if (others.Count > 0)
        {
            var othersTotal = others.Sum(i => i.Value);
            slices.Add(new ChartSlice((double)othersTotal, OthersColor));
            legend.Add(new LegendItem(Loc.Format("Main_Others", others.Count), OthersColor, Percent(othersTotal)));
        }

        return (slices, legend);
    }
}
