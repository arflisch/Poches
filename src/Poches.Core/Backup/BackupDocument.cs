using Poches.Core.Models;

namespace Poches.Core.Backup;

/// <summary>
/// Portable copy of all the user's data. Kept separate from the database entities so the
/// file format stays stable even if the storage schema evolves.
/// </summary>
public sealed record BackupDocument
{
    public const string FormatName = "poches-backup";
    /// <summary>Version 2 added <see cref="Subscriptions"/>.</summary>
    public const int CurrentVersion = 2;

    public string Format { get; init; } = FormatName;

    public int Version { get; init; } = CurrentVersion;

    public DateTime ExportedAt { get; init; }

    /// <summary>Display currency at export time; restored along with the data.</summary>
    public string? Currency { get; init; }

    public List<BackupPocket> Pockets { get; init; } = [];

    public List<BackupMovement> Movements { get; init; } = [];

    public List<BackupSubscription> Subscriptions { get; init; } = [];
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
    DateTime CreatedAt);
