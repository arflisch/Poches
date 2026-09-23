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
        var formatter = new MoneyFormatter("€");

        Assert.Equal("12 450,50 €", formatter.Format(12450.5m));
        Assert.Equal("+50,00 €", formatter.FormatSigned(50m));
        Assert.Equal("−12,30 €", formatter.FormatSigned(-12.3m));
        Assert.Equal("2 500 €", formatter.FormatShort(2500m));
        Assert.Equal(("12 450", ",07 €"), formatter.Split(12450.07m));
    }

    [Fact]
    public void Hides_amounts_in_privacy_mode()
    {
        var formatter = new MoneyFormatter("CHF", hideAmounts: true);

        Assert.Equal("••••• CHF", formatter.Format(99m));
        Assert.Equal(("•••••", " CHF"), formatter.Split(99m));
    }

    [Theory]
    [InlineData(0.25, "25 %")]
    [InlineData(0.004, "0,4 %")]
    [InlineData(1, "100 %")]
    public void Formats_percentages(double ratio, string expected)
    {
        Assert.Equal(expected, MoneyFormatter.FormatPercent(ratio));
    }
}
