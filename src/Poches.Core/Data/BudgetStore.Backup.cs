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

        return new BackupDocument
        {
            ExportedAt = now,
            Pockets = pockets
                .Select(p => new BackupPocket(p.Id, p.Name, p.Icon, p.ColorHex, p.GoalCents, p.CreatedAt))
                .ToList(),
            Movements = movements
                .Select(m => new BackupMovement(m.PocketId, m.AmountCents, m.Kind, m.Note, m.Date, m.TransferGroup, m.CounterpartPocketId))
                .ToList(),
        };
    }

    /// <summary>Replaces all current data with the content of <paramref name="backup"/>, atomically.</summary>
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
        });

        OnChanged();
    }

    /// <summary>Deletes every pocket and movement.</summary>
    public async Task DeleteAllAsync()
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<Movement>();
            conn.DeleteAll<Pocket>();
        });
        OnChanged();
    }

    private static void Validate(BackupDocument backup)
    {
        const string invalid = "Cette sauvegarde est incomplète ou abîmée, elle ne peut pas être restaurée.";

        var ids = new HashSet<int>();
        foreach (var pocket in backup.Pockets)
        {
            if (!ids.Add(pocket.Id) || string.IsNullOrWhiteSpace(pocket.Name))
                throw new BudgetException(invalid);
        }

        if (backup.Movements.Any(m => !ids.Contains(m.PocketId) || !Enum.IsDefined(m.Kind)))
            throw new BudgetException(invalid);
    }
}
