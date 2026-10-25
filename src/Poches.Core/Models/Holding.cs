using SQLite;

namespace Poches.Core.Models;

/// <summary>What kind of investment a holding is; the allocation chart can be grouped by it.</summary>
public enum HoldingType
{
    Etf = 0,
    Stock = 1,
    Bond = 2,
    Fund = 3,
    Crypto = 4,
    RealEstate = 5,
    Cash = 6,
    Other = 7,
}

/// <summary>
/// Where a holding is held, for the Belgian rules: pension and long-term savings and life insurance (branches 21
/// and 23) are outside the stock-exchange tax and the capital gains tax.
/// </summary>
public enum TaxEnvelope
{
    Standard = 0,
    PensionSavings = 1,
    LongTermSavings = 2,
    Branch21 = 3,
    Branch23 = 4,
}

/// <summary>
/// An investment line (an ETF, a share, a fund…). Its value comes from the live quote of <see cref="Symbol"/>
/// when online quotes are on, otherwise from <see cref="ManualPrice"/>, otherwise from the last purchase price.
/// </summary>
[Table("holdings")]
public sealed class Holding
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Ticker or ISIN as the user knows it, for reference only.</summary>
    [MaxLength(24)]
    public string? Code { get; set; }

    /// <summary>Yahoo Finance symbol (CW8.PA, PSP5.PA, AAPL…) used for online quotes.</summary>
    [MaxLength(24)]
    public string? Symbol { get; set; }

    public HoldingType Type { get; set; } = HoldingType.Etf;

    /// <summary>Where it is held, in the user's words: "PEA", "Compte-titres", "Assurance vie"…</summary>
    [MaxLength(40)]
    public string? Account { get; set; }

    [NotNull]
    public string ColorHex { get; set; } = "#6366F1";

    /// <summary>Price entered by hand, in millionths of the currency.</summary>
    public long? ManualPriceMicros { get; set; }

    public DateTime? ManualPriceDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public TaxEnvelope Envelope { get; set; }

    /// <summary>Belgian stock-exchange tax rate in millionths (1200 = 0.12 %); null: the usual rate for its type.</summary>
    public long? TobRateMicros { get; set; }

    /// <summary>Yearly running costs (TER) in millionths (2000 = 0.20 %).</summary>
    public long? TerMicros { get; set; }

    /// <summary>Target share of the portfolio in millionths (400000 = 40 %).</summary>
    public long? TargetMicros { get; set; }

    /// <summary>Value on 31 December 2025, the base of the Belgian capital gains tax for older units; null: from the price history.</summary>
    public long? ReferencePriceMicros { get; set; }

    /// <summary>A fund with more than 10 % of bonds, whose interest part is taxed when sold (Reynders tax).</summary>
    public bool Reynders { get; set; }

    [Ignore]
    public decimal? ManualPrice
    {
        get => ManualPriceMicros is { } micros ? Units.FromMicros(micros) : null;
        set => ManualPriceMicros = value is { } price ? Units.ToMicros(price) : null;
    }

    [Ignore]
    public decimal? TobRate
    {
        get => TobRateMicros is { } micros ? Units.FromMicros(micros) : null;
        set => TobRateMicros = value is { } rate ? Units.ToMicros(rate) : null;
    }

    [Ignore]
    public decimal? Ter
    {
        get => TerMicros is { } micros ? Units.FromMicros(micros) : null;
        set => TerMicros = value is { } ter ? Units.ToMicros(ter) : null;
    }

    [Ignore]
    public decimal? Target
    {
        get => TargetMicros is { } micros ? Units.FromMicros(micros) : null;
        set => TargetMicros = value is { } target ? Units.ToMicros(target) : null;
    }

    [Ignore]
    public decimal? ReferencePrice
    {
        get => ReferencePriceMicros is { } micros ? Units.FromMicros(micros) : null;
        set => ReferencePriceMicros = value is { } price ? Units.ToMicros(price) : null;
    }
}

/// <summary>
/// One purchase of a holding: a quantity bought on a date at a unit price, plus fees and stock-exchange tax. Amounts
/// are in the currency it was paid in: the portfolio's, or <see cref="Currency"/> at <see cref="FxRate"/>.
/// </summary>
[Table("purchases")]
public sealed class Purchase : IForeignOperation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int HoldingId { get; set; }

    public DateTime Date { get; set; }

    /// <summary>Quantity in millionths of a unit (fractional shares are common for ETFs).</summary>
    public long QuantityMicros { get; set; }

    /// <summary>Unit price in millionths of the currency.</summary>
    public long UnitPriceMicros { get; set; }

    public long FeesCents { get; set; }

    /// <summary>Belgian stock-exchange tax paid, in the purchase's currency.</summary>
    public long TobCents { get; set; }

    /// <summary>ISO code of the currency paid in, when not the portfolio's.</summary>
    [MaxLength(3)]
    public string? Currency { get; set; }

    /// <summary>Units of <see cref="Currency"/> for one unit of the portfolio currency, in millionths (as banks quote EUR/USD).</summary>
    public long? FxRateMicros { get; set; }

    /// <summary>The part paid with dividends received rather than new money, in the portfolio currency.</summary>
    public long? FromDividendsCents { get; set; }

    [Ignore]
    public decimal Quantity
    {
        get => Units.FromMicros(QuantityMicros);
        set => QuantityMicros = Units.ToMicros(value);
    }

    [Ignore]
    public decimal UnitPrice
    {
        get => Units.FromMicros(UnitPriceMicros);
        set => UnitPriceMicros = Units.ToMicros(value);
    }

    [Ignore]
    public decimal Fees
    {
        get => Money.FromCents(FeesCents);
        set => FeesCents = Money.ToCents(value);
    }

    [Ignore]
    public decimal Tob
    {
        get => Money.FromCents(TobCents);
        set => TobCents = Money.ToCents(value);
    }

    [Ignore]
    public decimal FromDividends
    {
        get => Money.FromCents(FromDividendsCents ?? 0);
        set => FromDividendsCents = value > 0 ? Money.ToCents(value) : null;
    }

    /// <summary>Divisor from the purchase's currency to the portfolio's: 1 when paid in the portfolio currency.</summary>
    [Ignore]
    public decimal Rate => Operations.Rate(this);

    /// <summary>Unit price in the portfolio currency.</summary>
    [Ignore]
    public decimal BaseUnitPrice => UnitPrice / Rate;

    /// <summary>What the purchase cost in its own currency, fees and tax included.</summary>
    [Ignore]
    public decimal NativeCost => Quantity * UnitPrice + Fees + Tob;

    /// <summary>What the purchase cost in the portfolio currency, fees and tax included.</summary>
    [Ignore]
    public decimal Invested => NativeCost / Rate;
}

/// <summary>
/// One sale of units of a holding. The units sold come from the oldest purchases still held (first in, first out).
/// Amounts are in the currency it was received in.
/// </summary>
[Table("sales")]
public sealed class Sale : IForeignOperation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int HoldingId { get; set; }

    public DateTime Date { get; set; }

    public long QuantityMicros { get; set; }

    public long UnitPriceMicros { get; set; }

    public long FeesCents { get; set; }

    public long TobCents { get; set; }

    [MaxLength(3)]
    public string? Currency { get; set; }

    public long? FxRateMicros { get; set; }

    [Ignore]
    public decimal Quantity
    {
        get => Units.FromMicros(QuantityMicros);
        set => QuantityMicros = Units.ToMicros(value);
    }

    [Ignore]
    public decimal UnitPrice
    {
        get => Units.FromMicros(UnitPriceMicros);
        set => UnitPriceMicros = Units.ToMicros(value);
    }

    [Ignore]
    public decimal Fees
    {
        get => Money.FromCents(FeesCents);
        set => FeesCents = Money.ToCents(value);
    }

    [Ignore]
    public decimal Tob
    {
        get => Money.FromCents(TobCents);
        set => TobCents = Money.ToCents(value);
    }

    [Ignore]
    public decimal Rate => Operations.Rate(this);

    [Ignore]
    public decimal BaseUnitPrice => UnitPrice / Rate;

    /// <summary>What reached the account in the sale's currency: the price less fees and tax.</summary>
    [Ignore]
    public decimal NativeProceeds => Quantity * UnitPrice - Fees - Tob;

    /// <summary>What reached the account in the portfolio currency.</summary>
    [Ignore]
    public decimal Proceeds => NativeProceeds / Rate;
}

/// <summary>A distribution received: the gross amount and the tax held back (Belgian withholding tax, foreign tax).</summary>
[Table("dividends")]
public sealed class Dividend : IForeignOperation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int HoldingId { get; set; }

    public DateTime Date { get; set; }

    public long GrossCents { get; set; }

    public long WithholdingCents { get; set; }

    [MaxLength(3)]
    public string? Currency { get; set; }

    public long? FxRateMicros { get; set; }

    [Ignore]
    public decimal Gross
    {
        get => Money.FromCents(GrossCents);
        set => GrossCents = Money.ToCents(value);
    }

    [Ignore]
    public decimal Withholding
    {
        get => Money.FromCents(WithholdingCents);
        set => WithholdingCents = Money.ToCents(value);
    }

    [Ignore]
    public decimal Rate => Operations.Rate(this);

    [Ignore]
    public decimal NativeNet => Gross - Withholding;

    /// <summary>Gross amount in the portfolio currency.</summary>
    [Ignore]
    public decimal BaseGross => Gross / Rate;

    /// <summary>Tax held back, in the portfolio currency.</summary>
    [Ignore]
    public decimal BaseWithholding => Withholding / Rate;

    /// <summary>What was received, in the portfolio currency.</summary>
    [Ignore]
    public decimal Net => NativeNet / Rate;
}

/// <summary>An operation that may be in another currency than the portfolio's.</summary>
public interface IForeignOperation
{
    string? Currency { get; }

    long? FxRateMicros { get; }
}

public static class Operations
{
    /// <summary>Currencies an operation can be in, besides the portfolio's.</summary>
    public static readonly IReadOnlyList<string> Currencies = ["EUR", "USD", "GBP", "CHF", "CAD", "JPY", "SEK", "NOK", "DKK", "AUD", "HKD", "PLN"];

    public const decimal MaxFxRate = 100_000m;

    public static decimal Rate(IForeignOperation operation) =>
        operation.Currency is not null && operation.FxRateMicros is { } micros && micros > 0 ? Units.FromMicros(micros) : 1m;

    public static bool IsForeign(IForeignOperation operation) => operation.Currency is not null && operation.FxRateMicros is > 0;
}

/// <summary>
/// The investment plan and goal, and the Belgian capital gains tax settings: one row. Amounts in the portfolio
/// currency, ratios in millionths.
/// </summary>
[Table("investment_settings")]
public sealed class InvestmentSettings
{
    public const int SingleId = 1;

    [PrimaryKey]
    public int Id { get; set; } = SingleId;

    /// <summary>Amount invested each month; null without a plan.</summary>
    public long? MonthlyCents { get; set; }

    /// <summary>Day of the month of the planned contribution (1 to 28).</summary>
    public int MonthlyDay { get; set; } = 5;

    /// <summary>Amount to reach; null without a goal.</summary>
    public long? GoalCents { get; set; }

    public DateTime? GoalDate { get; set; }

    /// <summary>Yearly return assumed for the projection (50000 = 5 %).</summary>
    public long ExpectedReturnMicros { get; set; } = 50_000;

    /// <summary>Yearly volatility assumed for the projection (150000 = 15 %).</summary>
    public long VolatilityMicros { get; set; } = 150_000;

    /// <summary>Belgian capital gains tax rate (100000 = 10 %).</summary>
    public long CapitalGainsRateMicros { get; set; } = 100_000;

    /// <summary>Yearly exemption of the capital gains tax.</summary>
    public long ExemptionCents { get; set; } = 1_000_000;

    [Ignore]
    public decimal? Monthly => MonthlyCents is { } c ? Money.FromCents(c) : null;

    [Ignore]
    public decimal? Goal => GoalCents is { } c ? Money.FromCents(c) : null;

    [Ignore]
    public decimal ExpectedReturn => Units.FromMicros(ExpectedReturnMicros);

    [Ignore]
    public decimal Volatility => Units.FromMicros(VolatilityMicros);

    [Ignore]
    public decimal CapitalGainsRate => Units.FromMicros(CapitalGainsRateMicros);

    [Ignore]
    public decimal Exemption => Money.FromCents(ExemptionCents);
}

/// <summary>Quantities and unit prices are stored as integer millionths, like amounts are stored as cents.</summary>
public static class Units
{
    public const decimal MaxQuantity = 1_000_000_000m;

    public static long ToMicros(decimal value) =>
        (long)decimal.Round(value * 1_000_000m, 0, MidpointRounding.AwayFromZero);

    public static decimal FromMicros(long micros) => micros / 1_000_000m;
}
