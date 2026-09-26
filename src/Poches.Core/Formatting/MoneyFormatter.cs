using System.Globalization;

namespace Poches.Core.Formatting;

/// <summary>An amount split for display: the cents are shown smaller than the whole part.</summary>
/// <param name="Prefix">Currency symbol when the culture puts it first ("€ " in Dutch), otherwise empty.</param>
/// <param name="Whole">Sign and whole part with group separators ("12 450").</param>
/// <param name="Fraction">Decimal separator, cents and, when the culture puts it last, the symbol (",00 €").</param>
public readonly record struct MoneyParts(string Prefix, string Whole, string Fraction);

/// <summary>
/// Formats amounts following the conventions of a culture ("12 450,00 €" in French, "€ 12.450,00" in Dutch,
/// "€12,450.00" in English) with a configurable currency symbol and a privacy mode.
/// </summary>
public sealed class MoneyFormatter
{
    public const string Mask = "•••••";

    private const string Nbsp = " ";
    private const string Minus = "−";

    private readonly NumberFormatInfo _format;
    private readonly CultureInfo _culture;

    public MoneyFormatter(string currencySymbol = "€", bool hideAmounts = false, CultureInfo? culture = null)
    {
        CurrencySymbol = currencySymbol;
        HideAmounts = hideAmounts;
        _culture = culture ?? CultureInfo.GetCultureInfo("fr-FR");
        _format = (NumberFormatInfo)_culture.NumberFormat.Clone();
        // French uses a narrow no-break space that some platform fonts render poorly.
        _format.NumberGroupSeparator = _format.NumberGroupSeparator.Replace(' ', ' ');
        _format.NegativeSign = Minus;
    }

    public string CurrencySymbol { get; }

    public bool HideAmounts { get; }

    public string DecimalSeparator => _format.NumberDecimalSeparator;

    // .NET currency patterns: 0 "$n", 1 "n$", 2 "$ n", 3 "n $".
    private bool SymbolFirst => _format.CurrencyPositivePattern is 0 or 2;

    private bool SymbolSpaced => _format.CurrencyPositivePattern is 2 or 3;

    public string Format(decimal amount) =>
        HideAmounts ? WithSymbol(Mask) : WithSymbol(Math.Abs(amount).ToString("N2", _format), amount < 0);

    /// <summary>Always shows the sign, using a true minus sign: "+50,00 €" / "−12,00 €".</summary>
    public string FormatSigned(decimal amount)
    {
        if (HideAmounts)
            return Format(amount);
        return (amount < 0 ? Minus : "+") + WithSymbol(Math.Abs(amount).ToString("N2", _format));
    }

    /// <summary>Compact form without cents when they are zero, for charts and goals: "2 500 €".</summary>
    public string FormatShort(decimal amount)
    {
        if (HideAmounts)
            return Format(amount);
        var absolute = Math.Abs(amount);
        return WithSymbol(absolute.ToString(absolute == decimal.Truncate(absolute) ? "N0" : "N2", _format), amount < 0);
    }

    /// <summary>Splits an amount so the cents can be displayed smaller than the whole part.</summary>
    public MoneyParts Split(decimal amount)
    {
        var prefix = SymbolFirst ? CurrencySymbol + (SymbolSpaced ? Nbsp : string.Empty) : string.Empty;
        var suffix = SymbolFirst ? string.Empty : (SymbolSpaced ? Nbsp : string.Empty) + CurrencySymbol;
        if (HideAmounts)
            return new MoneyParts(prefix, Mask, suffix);

        // Round first: averaged costs (a yearly price per month) can have fractions of a cent.
        var absolute = Math.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        var whole = decimal.Truncate(absolute).ToString("N0", _format);
        var cents = (int)(absolute % 1 * 100);
        var sign = amount < 0 ? Minus : string.Empty;
        return new MoneyParts(prefix, sign + whole, $"{_format.NumberDecimalSeparator}{cents:00}{suffix}");
    }

    /// <summary>"25 %" in French, "25%" in English and Dutch.</summary>
    public string FormatPercent(double ratio)
    {
        var number = (ratio * 100).ToString(ratio is > 0 and < 0.01 ? "0.#" : "0", _format);
        // .NET percent patterns: 0 "n %", 1 "n%", 2 "%n", 3 "% n".
        return _format.PercentPositivePattern switch
        {
            0 => number + Nbsp + "%",
            2 => "%" + number,
            3 => "%" + Nbsp + number,
            _ => number + "%",
        };
    }

    /// <summary>Formats the whole part of a number being typed ("1234" → "1 234").</summary>
    public string FormatWhole(long value) => value.ToString("N0", _format);

    private string WithSymbol(string number, bool negative = false)
    {
        var space = SymbolSpaced ? Nbsp : string.Empty;
        var text = SymbolFirst ? CurrencySymbol + space + number : number + space + CurrencySymbol;
        return negative ? Minus + text : text;
    }
}
