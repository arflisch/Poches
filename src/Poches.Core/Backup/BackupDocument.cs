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
    /// the scheduled deposits and split rules of Poches Pro.
    /// </summary>
    public const int CurrentVersion = 4;

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
