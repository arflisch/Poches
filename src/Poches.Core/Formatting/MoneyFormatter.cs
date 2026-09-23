using System.Globalization;

namespace Poches.Core.Formatting;

/// <summary>Formats amounts the French way ("12 450,00 €") with a configurable currency symbol and a privacy mode.</summary>
public sealed class MoneyFormatter
{
    public const string Mask = "•••••";

    private const string Nbsp = " ";
    private const string Minus = "−";

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly NumberFormatInfo _format;

    public MoneyFormatter(string currencySymbol = "€", bool hideAmounts = false)
    {
        CurrencySymbol = currencySymbol;
        HideAmounts = hideAmounts;
        _format = (NumberFormatInfo)French.NumberFormat.Clone();
        // A regular non-breaking space renders consistently in every platform font.
        _format.NumberGroupSeparator = Nbsp;
        _format.NegativeSign = Minus;
    }

    public string CurrencySymbol { get; }

    public bool HideAmounts { get; }

    public string Format(decimal amount) =>
        HideAmounts ? WithSymbol(Mask) : WithSymbol(amount.ToString("N2", _format));

    /// <summary>Always shows the sign, using a true minus sign: "+50,00 €" / "−12,00 €".</summary>
    public string FormatSigned(decimal amount)
    {
        if (HideAmounts)
            return Format(amount);
        var sign = amount < 0 ? Minus : "+";
        return sign + WithSymbol(Math.Abs(amount).ToString("N2", _format));
    }

    /// <summary>Compact form without cents when they are zero, for charts and goals: "2 500 €".</summary>
    public string FormatShort(decimal amount) =>
        HideAmounts
            ? Format(amount)
            : WithSymbol(amount.ToString(amount == decimal.Truncate(amount) ? "N0" : "N2", _format));

    /// <summary>Splits "12 450,00 €" into "12 450" and ",00 €" so the cents can be displayed smaller.</summary>
    public (string Whole, string Fraction) Split(decimal amount)
    {
        if (HideAmounts)
            return (Mask, Nbsp + CurrencySymbol);

        var absolute = Math.Abs(amount);
        var whole = decimal.Truncate(absolute).ToString("N0", _format);
        var cents = (int)(absolute % 1 * 100);
        var sign = amount < 0 ? Minus : string.Empty;
        return (sign + whole, WithSymbol($"{_format.NumberDecimalSeparator}{cents:00}"));
    }

    public static string FormatPercent(double ratio) =>
        (ratio * 100).ToString(ratio is > 0 and < 0.01 ? "0.#" : "0", French) + Nbsp + "%";

    private string WithSymbol(string number) => number + Nbsp + CurrencySymbol;
}
