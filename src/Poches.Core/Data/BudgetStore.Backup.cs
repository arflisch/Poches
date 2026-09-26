using Poches.Core.Backup;
using Poches.Core.Models;

namespace Poches.Core.Data;

public sealed partial class BudgetStore
{
    public async Task<BackupDocument> ExportAsync(DateTime now)
    {
        var db = await GetConnectionAsync();
        var pockets = await db.Table<Pocket>().OrderBy(p => p.Id).ToListAsync();
        var movements = await db.Table<Movement>().OrderBy(m => m.Date).ThenBy(m => m.Id).ToListAsync();
        var subscriptions = await db.Table<Subscription>().OrderBy(s => s.Id).ToListAsync();

        return new BackupDocument
        {
            ExportedAt = now,
            Pockets = pockets
                .Select(p => new BackupPocket(p.Id, p.Name, p.Icon, p.ColorHex, p.GoalCents, p.CreatedAt))
                .ToList(),
            Movements = movements
                .Select(m => new BackupMovement(m.PocketId, m.AmountCents, m.Kind, m.Note, m.Date, m.TransferGroup, m.CounterpartPocketId))
                .ToList(),
            Subscriptions = subscriptions
                .Select(s => new BackupSubscription(s.Name, s.Icon, s.ColorHex, s.AmountCents, s.Period, s.BillingAnchor, s.IsActive, s.CreatedAt))
                .ToList(),
        };
    }

    /// <summary>
    /// Replaces all current data with the content of <paramref name="backup"/>, atomically. Backups made before
    /// subscriptions existed (version 1) leave the current subscriptions untouched.
    /// </summary>
    /// <exception cref="BudgetException">The backup is inconsistent; nothing is changed.</exception>
    public async Task ImportAsync(BackupDocument backup)
    {
        Validate(backup);
        var db = await GetConnectionAsync();

        await db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<Movement>();
            conn.DeleteAll<Pocket>();

            // Database ids are regenerated, so remember where each backed-up pocket landed.
            var newIds = new Dictionary<int, int>();
            foreach (var p in backup.Pockets)
            {
                var pocket = new Pocket
                {
                    Name = p.Name.Trim(),
                    Icon = string.IsNullOrWhiteSpace(p.Icon) ? "💰" : p.Icon,
                    ColorHex = string.IsNullOrWhiteSpace(p.ColorHex) ? "#6366F1" : p.ColorHex,
                    GoalCents = p.GoalCents is > 0 ? p.GoalCents : null,
                    CreatedAt = p.CreatedAt,
                };
                conn.Insert(pocket);
                newIds[p.Id] = pocket.Id;
            }

            foreach (var m in backup.Movements)
            {
                conn.Insert(new Movement
                {
                    PocketId = newIds[m.PocketId],
                    AmountCents = m.AmountCents,
                    Kind = m.Kind,
                    Note = m.Note,
                    Date = m.Date,
                    TransferGroup = m.TransferGroup,
                    CounterpartPocketId = m.CounterpartPocketId is { } id && newIds.TryGetValue(id, out var mapped) ? mapped : null,
                });
            }

            if (backup.Version >= 2)
            {
                conn.DeleteAll<Subscription>();
                foreach (var s in backup.Subscriptions)
                {
                    conn.Insert(new Subscription
                    {
                        Name = s.Name.Trim(),
                        Icon = string.IsNullOrWhiteSpace(s.Icon) ? "📺" : s.Icon,
                        ColorHex = string.IsNullOrWhiteSpace(s.ColorHex) ? "#6366F1" : s.ColorHex,
                        AmountCents = s.AmountCents,
                        Period = s.Period,
                        BillingAnchor = s.BillingAnchor.Date,
                        IsActive = s.IsActive,
                        CreatedAt = s.CreatedAt,
                    });
                }
            }
        });

        OnChanged();
    }

    /// <summary>Deletes every pocket, movement and subscription.</summary>
    public async Task DeleteAllAsync()
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<Movement>();
            conn.DeleteAll<Pocket>();
            conn.DeleteAll<Subscription>();
        });
        OnChanged();
    }

    private static void Validate(BackupDocument backup)
    {
        var ids = new HashSet<int>();
        foreach (var pocket in backup.Pockets)
        {
            if (!ids.Add(pocket.Id) || string.IsNullOrWhiteSpace(pocket.Name))
                throw new BudgetException(BudgetError.CorruptBackup, $"Invalid or duplicate pocket {pocket.Id}.");
        }

        if (backup.Movements.Any(m => !ids.Contains(m.PocketId) || !Enum.IsDefined(m.Kind)))
            throw new BudgetException(BudgetError.CorruptBackup, "A movement references an unknown pocket or kind.");

        if (backup.Subscriptions.Any(s => string.IsNullOrWhiteSpace(s.Name) || s.AmountCents <= 0 || !Enum.IsDefined(s.Period)))
            throw new BudgetException(BudgetError.CorruptBackup, "A subscription is incomplete.");
    }
}
