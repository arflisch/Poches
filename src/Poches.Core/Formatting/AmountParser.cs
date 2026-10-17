using System.Globalization;

namespace Poches.Core.Formatting;

/// <summary>Parses amounts typed by the user, accepting both "12,50" and "12.50" and ignoring spaces or currency symbols.</summary>
public static class AmountParser
{
    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = new string(text.Where(c => char.IsAsciiDigit(c) || c is ',' or '.').ToArray());
        if (cleaned.Length == 0)
            return false;

        // The last separator is the decimal one; any earlier separator is a thousands separator.
        var lastSeparator = cleaned.LastIndexOfAny([',', '.']);
        if (lastSeparator >= 0)
        {
            var integerPart = cleaned[..lastSeparator].Replace(",", string.Empty).Replace(".", string.Empty);
            var decimalPart = cleaned[(lastSeparator + 1)..];
            if (decimalPart.Length > 2)
                return false;
            cleaned = $"{(integerPart.Length == 0 ? "0" : integerPart)}.{(decimalPart.Length == 0 ? "0" : decimalPart)}";
        }

        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }

    /// <summary>
    /// Parses a quantity or a unit price, which can have more decimals than an amount ("29,4002", "0.000123").
    /// The last separator is the decimal one; spaces and currency symbols are ignored.
    /// </summary>
    public static bool TryParseDecimal(string? text, int maxDecimals, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = new string(text.Where(c => char.IsAsciiDigit(c) || c is ',' or '.').ToArray());
        if (cleaned.Length == 0)
            return false;

        var lastSeparator = cleaned.LastIndexOfAny([',', '.']);
        if (lastSeparator >= 0)
        {
            var integerPart = cleaned[..lastSeparator].Replace(",", string.Empty).Replace(".", string.Empty);
            var decimalPart = cleaned[(lastSeparator + 1)..];
            if (decimalPart.Length > maxDecimals)
                return false;
            cleaned = $"{(integerPart.Length == 0 ? "0" : integerPart)}.{(decimalPart.Length == 0 ? "0" : decimalPart)}";
        }

        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
