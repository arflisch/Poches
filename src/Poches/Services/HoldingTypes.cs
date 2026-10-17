using System.Globalization;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Localization;

namespace Poches.Services;

/// <summary>Display name, emoji and chart colour of each <see cref="HoldingType"/>.</summary>
public static class HoldingTypes
{
    public static readonly IReadOnlyList<HoldingType> All =
    [
        HoldingType.Etf, HoldingType.Stock, HoldingType.Bond, HoldingType.Fund,
        HoldingType.Crypto, HoldingType.RealEstate, HoldingType.Cash, HoldingType.Other,
    ];

    public static string Name(HoldingType type) => type switch
    {
        HoldingType.Etf => Loc.Get("HoldingType_Etf"),
        HoldingType.Stock => Loc.Get("HoldingType_Stock"),
        HoldingType.Bond => Loc.Get("HoldingType_Bond"),
        HoldingType.Fund => Loc.Get("HoldingType_Fund"),
        HoldingType.Crypto => Loc.Get("HoldingType_Crypto"),
        HoldingType.RealEstate => Loc.Get("HoldingType_RealEstate"),
        HoldingType.Cash => Loc.Get("HoldingType_Cash"),
        _ => Loc.Get("HoldingType_Other"),
    };

    public static string Icon(HoldingType type) => type switch
    {
        HoldingType.Etf => "📊",
        HoldingType.Stock => "🏢",
        HoldingType.Bond => "📜",
        HoldingType.Fund => "🏦",
        HoldingType.Crypto => "🪙",
        HoldingType.RealEstate => "🏠",
        HoldingType.Cash => "💶",
        _ => "💼",
    };

    public static Color Color(HoldingType type) => Microsoft.Maui.Graphics.Color.FromArgb(type switch
    {
        HoldingType.Etf => "#6366F1",
        HoldingType.Stock => "#0891B2",
        HoldingType.Bond => "#D97706",
        HoldingType.Fund => "#10B981",
        HoldingType.Crypto => "#F97316",
        HoldingType.RealEstate => "#8B5CF6",
        HoldingType.Cash => "#65A30D",
        _ => "#475569",
    });

    /// <summary>Yahoo's quote type (ETF, EQUITY…) as a holding type.</summary>
    public static HoldingType FromKind(string kind) => kind switch
    {
        "ETF" => HoldingType.Etf,
        "EQUITY" => HoldingType.Stock,
        "MUTUALFUND" => HoldingType.Fund,
        "CRYPTOCURRENCY" => HoldingType.Crypto,
        _ => HoldingType.Other,
    };

    /// <summary>"Amundi Index Solutions - Amundi MSCI World Swap UCITS ETF" → the fund's own name.</summary>
    public static string ShortName(string name)
    {
        var parts = name.Split(" - ");
        var tail = parts[^1].Trim();
        return parts.Length > 1 && tail.Length >= 10 ? tail : name;
    }
}

/// <summary>Number formats of the investment screens, following the current language.</summary>
public static class InvestmentText
{
    private static CultureInfo Culture => Localizer.Instance.Culture;

    /// <summary>"+7,3 %" / "−4,2 %" with one decimal, in the language's percent pattern.</summary>
    public static string SignedPercent(double ratio)
    {
        var rounded = Math.Round(ratio * 100, 1);
        var sign = rounded > 0 ? "+" : rounded < 0 ? "−" : string.Empty;
        return sign + Percent(Math.Abs(rounded) / 100);
    }

    /// <summary>"7,3 %" with one decimal.</summary>
    public static string Percent(double ratio)
    {
        var number = (ratio * 100).ToString("0.0", Culture);
        return Culture.NumberFormat.PercentPositivePattern switch
        {
            0 => number + " %",
            2 => "%" + number,
            3 => "% " + number,
            _ => number + "%",
        };
    }

    /// <summary>Unit prices keep up to four decimals ("0,0012 €", "52,40 €").</summary>
    public static string Price(decimal price, MoneyFormatter formatter)
    {
        if (formatter.HideAmounts || price >= 1 || price == 0)
            return formatter.Format(price);
        var number = price.ToString("0.00##", Culture);
        var probe = formatter.Format(0m);
        var zero = 0m.ToString("N2", Culture);
        return probe.Replace(zero, number, StringComparison.Ordinal);
    }

    public static string Quantity(decimal quantity) => quantity.ToString("#,0.####", Culture);

    /// <summary>Amount being typed back into a field ("29,4002").</summary>
    public static string Editable(decimal value, int decimals = 6) =>
        decimal.Round(value, decimals).ToString("0.######", Culture);
}
