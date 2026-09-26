using System.Globalization;
using Poches.Core.Formatting;

namespace Poches.Core.Tests;

public sealed class FormattingTests
{
    [Theory]
    [InlineData("12", 12)]
    [InlineData("12,5", 12.5)]
    [InlineData("12.50", 12.5)]
    [InlineData(" 1 234,56 € ", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData(",5", 0.5)]
    [InlineData("7,", 7)]
    public void Parses_user_amounts(string input, double expected)
    {
        Assert.True(AmountParser.TryParse(input, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12,345")]
    [InlineData(null)]
    public void Rejects_invalid_amounts(string? input)
    {
        Assert.False(AmountParser.TryParse(input, out _));
    }

    [Fact]
    public void Formats_french_style()
    {
        var formatter = new MoneyFormatter("€", culture: CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal("12\u00A0450,50\u00A0€", formatter.Format(12450.5m));
        Assert.Equal("+50,00\u00A0€", formatter.FormatSigned(50m));
        Assert.Equal("\u221212,30\u00A0€", formatter.FormatSigned(-12.3m));
        Assert.Equal("2\u00A0500\u00A0€", formatter.FormatShort(2500m));
        Assert.Equal(new MoneyParts("", "12\u00A0450", ",07\u00A0€"), formatter.Split(12450.07m));
        Assert.Equal(",", formatter.DecimalSeparator);
    }

    [Fact]
    public void Split_rounds_fractions_of_a_cent()
    {
        var formatter = new MoneyFormatter("€", culture: CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal(new MoneyParts("", "78", ",33\u00A0€"), formatter.Split(78.325m));
        Assert.Equal(new MoneyParts("", "100", ",00\u00A0€"), formatter.Split(99.999m));
        Assert.Equal(formatter.Format(78.325m), string.Concat(formatter.Split(78.325m).Whole, formatter.Split(78.325m).Fraction));
    }

    [Fact]
    public void Formats_dutch_style_with_the_symbol_first()
    {
        var formatter = new MoneyFormatter("€", culture: CultureInfo.GetCultureInfo("nl-NL"));

        Assert.Equal("€\u00A012.450,50", formatter.Format(12450.5m));
        Assert.Equal("+€\u00A050,00", formatter.FormatSigned(50m));
        Assert.Equal("\u2212€\u00A012,30", formatter.Format(-12.3m));
        Assert.Equal(new MoneyParts("€\u00A0", "12.450", ",07"), formatter.Split(12450.07m));
    }

    [Fact]
    public void Formats_english_style()
    {
        var formatter = new MoneyFormatter("€", culture: CultureInfo.GetCultureInfo("en-GB"));

        Assert.Equal("€12,450.50", formatter.Format(12450.5m));
        Assert.Equal("€2,500", formatter.FormatShort(2500m));
        Assert.Equal(new MoneyParts("€", "12,450", ".07"), formatter.Split(12450.07m));
        Assert.Equal(".", formatter.DecimalSeparator);
    }

    [Fact]
    public void Hides_amounts_in_privacy_mode()
    {
        var formatter = new MoneyFormatter("CHF", hideAmounts: true, culture: CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal("•••••\u00A0CHF", formatter.Format(99m));
        Assert.Equal(new MoneyParts("", "•••••", "\u00A0CHF"), formatter.Split(99m));
    }

    [Theory]
    [InlineData("fr-FR", 0.25, "25\u00A0%")]
    [InlineData("fr-FR", 0.004, "0,4\u00A0%")]
    [InlineData("en-GB", 1, "100%")]
    [InlineData("nl-NL", 0.004, "0,4%")]
    public void Formats_percentages(string culture, double ratio, string expected)
    {
        var formatter = new MoneyFormatter(culture: CultureInfo.GetCultureInfo(culture));
        Assert.Equal(expected, formatter.FormatPercent(ratio));
    }
}
