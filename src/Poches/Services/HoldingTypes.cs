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

    public static string EnvelopeName(TaxEnvelope envelope) => envelope switch
    {
        TaxEnvelope.PensionSavings => Loc.Get("Envelope_PensionSavings"),
        TaxEnvelope.LongTermSavings => Loc.Get("Envelope_LongTermSavings"),
        TaxEnvelope.Branch21 => Loc.Get("Envelope_Branch21"),
        TaxEnvelope.Branch23 => Loc.Get("Envelope_Branch23"),
        _ => Loc.Get("Envelope_Standard"),
    };

    /// <summary>Exposure keys of the analysis (Yahoo's sectors, asset classes, neutral keys); countries keep Yahoo's English names.</summary>
    public static string ExposureName(string key) => key switch
    {
        "unknown" => Loc.Get("Exposure_Unknown"),
        "funds" => Loc.Get("Exposure_Funds"),
        "non_equity" => Loc.Get("Exposure_NonEquity"),
        "unlisted" => Loc.Get("Exposure_Unlisted"),
        "stocks" => Loc.Get("Exposure_Stocks"),
        "bonds" => Loc.Get("Exposure_Bonds"),
        "cash" => Loc.Get("Exposure_Cash"),
        "other" => Loc.Get("Exposure_Other"),
        "realestate" => Loc.Get("Exposure_RealEstate"),
        "crypto" => Loc.Get("Exposure_Crypto"),
        "technology" => Loc.Get("Sector_Technology"),
        "financial_services" => Loc.Get("Sector_Financials"),
        "healthcare" => Loc.Get("Sector_Healthcare"),
        "consumer_cyclical" => Loc.Get("Sector_ConsumerCyclical"),
        "consumer_defensive" => Loc.Get("Sector_ConsumerDefensive"),
        "industrials" => Loc.Get("Sector_Industrials"),
        "communication_services" => Loc.Get("Sector_Communication"),
        "energy" => Loc.Get("Sector_Energy"),
        "utilities" => Loc.Get("Sector_Utilities"),
        "basic_materials" => Loc.Get("Sector_Materials"),
        _ => key.Length > 0 ? char.ToUpper(key[0], CultureInfo.CurrentCulture) + key[1..].Replace('_', ' ') : key,
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

    /// <summary>"7,3 %" with one decimal, or more for small rates ("0,12 %").</summary>
    public static string Percent(double ratio, int decimals = 1)
    {
        var number = (ratio * 100).ToString("0." + new string('0', decimals), Culture);
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
