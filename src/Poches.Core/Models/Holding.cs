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

    [Ignore]
    public decimal? ManualPrice
    {
        get => ManualPriceMicros is { } micros ? Units.FromMicros(micros) : null;
        set => ManualPriceMicros = value is { } price ? Units.ToMicros(price) : null;
    }
}

/// <summary>One purchase of a holding: a quantity bought on a date at a unit price, plus fees.</summary>
[Table("purchases")]
public sealed class Purchase
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

    /// <summary>What the purchase cost, fees included.</summary>
    [Ignore]
    public decimal Invested => Quantity * UnitPrice + Fees;
}

/// <summary>Quantities and unit prices are stored as integer millionths, like amounts are stored as cents.</summary>
public static class Units
{
    public const decimal MaxQuantity = 1_000_000_000m;

    public static long ToMicros(decimal value) =>
        (long)decimal.Round(value * 1_000_000m, 0, MidpointRounding.AwayFromZero);

    public static decimal FromMicros(long micros) => micros / 1_000_000m;
}
