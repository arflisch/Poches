using SQLite;

namespace Poches.Core.Models;

/// <summary>The last quote fetched for a symbol, in its own currency. Market data, not user data.</summary>
[Table("quote_cache")]
public sealed class CachedQuote
{
    [PrimaryKey, MaxLength(24)]
    public string Symbol { get; set; } = string.Empty;

    public double Price { get; set; }

    public double? PreviousClose { get; set; }

    [NotNull, MaxLength(8)]
    public string Currency { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? Name { get; set; }

    /// <summary>When the market priced it (UTC).</summary>
    public DateTime AsOf { get; set; }

    /// <summary>When Poches asked for it (UTC).</summary>
    public DateTime FetchedAt { get; set; }
}

/// <summary>One daily close, in the symbol's own currency.</summary>
[Table("price_history")]
public sealed class PriceClose
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "ix_price_history_symbol_date", Order = 1, Unique = true), NotNull, MaxLength(24)]
    public string Symbol { get; set; } = string.Empty;

    [Indexed(Name = "ix_price_history_symbol_date", Order = 2, Unique = true)]
    public DateTime Date { get; set; }

    public double Close { get; set; }
}

/// <summary>What <see cref="PriceClose"/> holds for a symbol: closes from <see cref="CoveredFrom"/>, fetched at <see cref="FetchedAt"/>.</summary>
[Table("price_coverage")]
public sealed class PriceCoverage
{
    [PrimaryKey, MaxLength(24)]
    public string Symbol { get; set; } = string.Empty;

    [NotNull, MaxLength(8)]
    public string Currency { get; set; } = string.Empty;

    public DateTime CoveredFrom { get; set; }

    public DateTime FetchedAt { get; set; }
}
