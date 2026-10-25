using Poches.Core.Models;

namespace Poches.Core.Backup;

/// <summary>
/// Portable copy of all the user's data. Kept separate from the database entities so the
/// file format stays stable even if the storage schema evolves.
/// </summary>
public sealed record BackupDocument
{
    public const string FormatName = "poches-backup";
    /// <summary>
    /// Version 2 added <see cref="Subscriptions"/>; version 3 their category and the 6-month period; version 4
    /// the scheduled deposits and split rules of Poches Pro; version 5 the investments (holdings and purchases);
    /// version 6 sales, dividends, currencies, the Belgian tax fields, and the investment plan and goal.
    /// </summary>
    public const int CurrentVersion = 6;

    public string Format { get; init; } = FormatName;

    public int Version { get; init; } = CurrentVersion;

    public DateTime ExportedAt { get; init; }

    /// <summary>Display currency at export time; restored along with the data.</summary>
    public string? Currency { get; init; }

    public List<BackupPocket> Pockets { get; init; } = [];

    public List<BackupMovement> Movements { get; init; } = [];

    public List<BackupSubscription> Subscriptions { get; init; } = [];

    public List<BackupScheduledDeposit> ScheduledDeposits { get; init; } = [];

    public List<BackupSplitRule> SplitRules { get; init; } = [];

    public List<BackupHolding> Holdings { get; init; } = [];

    public List<BackupPurchase> Purchases { get; init; } = [];

    public List<BackupSale> Sales { get; init; } = [];

    public List<BackupDividend> Dividends { get; init; } = [];

    /// <summary>Investment plan, goal and tax settings; null when never set or before version 6.</summary>
    public BackupInvestmentSettings? InvestmentSettings { get; init; }
}

public sealed record BackupPocket(
    int Id,
    string Name,
    string Icon,
    string ColorHex,
    long? GoalCents,
    DateTime CreatedAt);

public sealed record BackupMovement(
    int PocketId,
    long AmountCents,
    MovementKind Kind,
    string? Note,
    DateTime Date,
    string? TransferGroup,
    int? CounterpartPocketId);

public sealed record BackupSubscription(
    string Name,
    string Icon,
    string ColorHex,
    long AmountCents,
    BillingPeriod Period,
    DateTime BillingAnchor,
    bool IsActive,
    DateTime CreatedAt,
    ChargeCategory Category = ChargeCategory.Subscription);

public sealed record BackupScheduledDeposit(
    int PocketId,
    long AmountCents,
    BillingPeriod Period,
    DateTime Anchor,
    DateTime AppliedThrough,
    string? Note,
    bool IsActive,
    DateTime CreatedAt);

public sealed record BackupSplitRule(int PocketId, int BasisPoints);

public sealed record BackupHolding(
    int Id,
    string Name,
    string? Code,
    string? Symbol,
    HoldingType Type,
    string? Account,
    string ColorHex,
    long? ManualPriceMicros,
    DateTime? ManualPriceDate,
    DateTime CreatedAt,
    TaxEnvelope Envelope = TaxEnvelope.Standard,
    long? TobRateMicros = null,
    long? TerMicros = null,
    long? TargetMicros = null,
    long? ReferencePriceMicros = null,
    bool Reynders = false);

public sealed record BackupPurchase(
    int HoldingId,
    DateTime Date,
    long QuantityMicros,
    long UnitPriceMicros,
    long FeesCents,
    long TobCents = 0,
    string? Currency = null,
    long? FxRateMicros = null,
    long? FromDividendsCents = null);

public sealed record BackupSale(
    int HoldingId,
    DateTime Date,
    long QuantityMicros,
    long UnitPriceMicros,
    long FeesCents,
    long TobCents,
    string? Currency,
    long? FxRateMicros);

public sealed record BackupDividend(int HoldingId, DateTime Date, long GrossCents, long WithholdingCents, string? Currency, long? FxRateMicros);

public sealed record BackupInvestmentSettings(
    long? MonthlyCents,
    int MonthlyDay,
    long? GoalCents,
    DateTime? GoalDate,
    long ExpectedReturnMicros,
    long VolatilityMicros,
    long CapitalGainsRateMicros,
    long ExemptionCents);
