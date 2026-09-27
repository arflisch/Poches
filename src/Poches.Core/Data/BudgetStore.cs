using Poches.Core.Models;
using SQLite;

namespace Poches.Core.Data;

/// <summary>
/// Single entry point to the local SQLite database. Balances are never stored:
/// they are always the sum of a pocket's movements, so they cannot drift.
/// </summary>
public sealed partial class BudgetStore(string databasePath) : IAsyncDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    /// <summary>Raised after any write, so screens can refresh.</summary>
    public event EventHandler? Changed;

    public async Task<BudgetOverview> GetOverviewAsync(DateTime now)
    {
        var db = await GetConnectionAsync();
        var pockets = await GetPocketSummariesAsync(db);

        var monthStart = new DateTime(now.Year, now.Month, 1);
        var monthDelta = await db.ExecuteScalarAsync<long>(
            "SELECT IFNULL(SUM(AmountCents), 0) FROM movements WHERE Date >= ? AND Kind IN (?, ?)",
            monthStart, (int)MovementKind.Deposit, (int)MovementKind.Withdrawal);

        return new BudgetOverview(pockets, pockets.Sum(p => p.BalanceCents), monthDelta);
    }

    public async Task<IReadOnlyList<PocketSummary>> GetPocketSummariesAsync() =>
        await GetPocketSummariesAsync(await GetConnectionAsync());

    public async Task<PocketSummary?> GetPocketSummaryAsync(int pocketId)
    {
        var db = await GetConnectionAsync();
        var pocket = await db.FindAsync<Pocket>(pocketId);
        return pocket is null ? null : new PocketSummary(pocket, await GetBalanceCentsAsync(db, pocketId));
    }

    public async Task<IReadOnlyList<Movement>> GetMovementsAsync(int pocketId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Movement>()
            .Where(m => m.PocketId == pocketId)
            .OrderByDescending(m => m.Date)
            .ThenByDescending(m => m.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Creates or updates a pocket. A positive <paramref name="initialAmount"/> is recorded as a first deposit
    /// labelled <paramref name="initialNote"/>.
    /// </summary>
    public async Task<Pocket> SavePocketAsync(Pocket pocket, decimal initialAmount = 0m, string? initialNote = null)
    {
        pocket.Name = pocket.Name.Trim();
        if (pocket.Name.Length == 0)
            throw new BudgetException(BudgetError.EmptyName, "A pocket needs a name.");
        if (pocket.GoalCents is <= 0)
            pocket.GoalCents = null;
        if (initialAmount < 0 || initialAmount > Money.MaxAmount)
            throw new BudgetException(BudgetError.InvalidInitialAmount, $"Invalid initial amount {initialAmount}.");

        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            if (pocket.Id == 0)
            {
                pocket.CreatedAt = DateTime.Now;
                conn.Insert(pocket);
                if (initialAmount > 0)
                {
                    conn.Insert(new Movement
                    {
                        PocketId = pocket.Id,
                        AmountCents = Money.ToCents(initialAmount),
                        Kind = MovementKind.Deposit,
                        Note = Normalize(initialNote),
                        Date = DateTime.Now,
                    });
                }
            }
            else
            {
                conn.Update(pocket);
            }
        });

        OnChanged();
        return pocket;
    }

    /// <summary>Deletes a pocket and its history. Transfers keep their other leg in the counterpart pocket.</summary>
    public async Task DeletePocketAsync(int pocketId)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM movements WHERE PocketId = ?", pocketId);
            conn.Execute("DELETE FROM scheduled_deposits WHERE PocketId = ?", pocketId);
            conn.Execute("DELETE FROM split_rules WHERE PocketId = ?", pocketId);
            conn.Execute("UPDATE movements SET CounterpartPocketId = NULL WHERE CounterpartPocketId = ?", pocketId);
            conn.Delete<Pocket>(pocketId);
        });
        OnChanged();
    }

    public async Task<Movement> AddMovementAsync(int pocketId, MovementKind kind, decimal amount, string? note, DateTime date)
    {
        if (kind is not (MovementKind.Deposit or MovementKind.Withdrawal))
            throw new ArgumentOutOfRangeException(nameof(kind), "Use TransferAsync for transfers.");
        EnsureValidAmount(amount);

        var db = await GetConnectionAsync();
        await EnsurePocketExistsAsync(db, pocketId);

        var cents = Money.ToCents(amount);
        if (kind == MovementKind.Withdrawal && await GetBalanceCentsAsync(db, pocketId) < cents)
            throw new BudgetException(BudgetError.InsufficientFunds, $"Pocket {pocketId} cannot cover a withdrawal of {amount}.");

        var movement = new Movement
        {
            PocketId = pocketId,
            AmountCents = kind == MovementKind.Deposit ? cents : -cents,
            Kind = kind,
            Note = Normalize(note),
            Date = date,
        };
        await db.InsertAsync(movement);
        OnChanged();
        return movement;
    }

    public async Task TransferAsync(int fromPocketId, int toPocketId, decimal amount, string? note, DateTime date)
    {
        if (fromPocketId == toPocketId)
            throw new BudgetException(BudgetError.SamePocket, "A transfer needs two different pockets.");
        EnsureValidAmount(amount);

        var db = await GetConnectionAsync();
        await EnsurePocketExistsAsync(db, fromPocketId);
        await EnsurePocketExistsAsync(db, toPocketId);

        var cents = Money.ToCents(amount);
        if (await GetBalanceCentsAsync(db, fromPocketId) < cents)
            throw new BudgetException(BudgetError.InsufficientFunds, $"Pocket {fromPocketId} cannot cover a transfer of {amount}.");

        var group = Guid.NewGuid().ToString("N");
        await db.RunInTransactionAsync(conn =>
        {
            conn.Insert(new Movement
            {
                PocketId = fromPocketId,
                AmountCents = -cents,
                Kind = MovementKind.TransferOut,
                Note = Normalize(note),
                Date = date,
                TransferGroup = group,
                CounterpartPocketId = toPocketId,
            });
            conn.Insert(new Movement
            {
                PocketId = toPocketId,
                AmountCents = cents,
                Kind = MovementKind.TransferIn,
                Note = Normalize(note),
                Date = date,
                TransferGroup = group,
                CounterpartPocketId = fromPocketId,
            });
        });
        OnChanged();
    }

    /// <summary>Deletes a movement (both legs for a transfer), refusing if a pocket would go negative.</summary>
    public async Task DeleteMovementAsync(int movementId)
    {
        var db = await GetConnectionAsync();
        var movement = await db.FindAsync<Movement>(movementId)
            ?? throw new BudgetException(BudgetError.MovementNotFound, $"Movement {movementId} does not exist.");

        var legs = movement.TransferGroup is { } group
            ? await db.Table<Movement>().Where(m => m.TransferGroup == group).ToListAsync()
            : [movement];

        foreach (var leg in legs.Where(l => l.AmountCents > 0))
        {
            if (await GetBalanceCentsAsync(db, leg.PocketId) - leg.AmountCents < 0)
                throw new BudgetException(BudgetError.BalanceWouldBeNegative, $"Deleting would make pocket {leg.PocketId} negative.");
        }

        await db.RunInTransactionAsync(conn =>
        {
            foreach (var leg in legs)
                conn.Delete<Movement>(leg.Id);
        });
        OnChanged();
    }

    /// <summary>Running balance after each day with activity, oldest first.</summary>
    public static IReadOnlyList<BalancePoint> BuildBalanceHistory(IEnumerable<Movement> movements)
    {
        var points = new List<BalancePoint>();
        long running = 0;
        foreach (var day in movements.GroupBy(m => m.Date.Date).OrderBy(g => g.Key))
        {
            running += day.Sum(m => m.AmountCents);
            points.Add(new BalancePoint(day.Key, Money.FromCents(running)));
        }
        return points;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection = null;
        }
        _initLock.Dispose();
    }

    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
            return _connection;

        await _initLock.WaitAsync();
        try
        {
            if (_connection is null)
            {
                var connection = new SQLiteAsyncConnection(
                    databasePath,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
                await connection.EnableWriteAheadLoggingAsync();
                await connection.CreateTablesAsync<Pocket, Movement, Subscription, ScheduledDeposit, SplitRule>();
                _connection = connection;
            }
            return _connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task<IReadOnlyList<PocketSummary>> GetPocketSummariesAsync(SQLiteAsyncConnection db)
    {
        var pockets = await db.Table<Pocket>().ToListAsync();
        var balances = await db.QueryAsync<BalanceRow>(
            "SELECT PocketId, SUM(AmountCents) AS BalanceCents FROM movements GROUP BY PocketId");
        var byPocket = balances.ToDictionary(b => b.PocketId, b => b.BalanceCents);

        return pockets
            .Select(p => new PocketSummary(p, byPocket.GetValueOrDefault(p.Id)))
            .OrderByDescending(p => p.BalanceCents)
            .ThenBy(p => p.Pocket.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static Task<long> GetBalanceCentsAsync(SQLiteAsyncConnection db, int pocketId) =>
        db.ExecuteScalarAsync<long>("SELECT IFNULL(SUM(AmountCents), 0) FROM movements WHERE PocketId = ?", pocketId);

    private static async Task EnsurePocketExistsAsync(SQLiteAsyncConnection db, int pocketId)
    {
        if (await db.FindAsync<Pocket>(pocketId) is null)
            throw new BudgetException(BudgetError.PocketNotFound, $"Pocket {pocketId} does not exist.");
    }

    private static void EnsureValidAmount(decimal amount)
    {
        if (amount <= 0)
            throw new BudgetException(BudgetError.InvalidAmount, $"Amount {amount} must be positive.");
        if (amount > Money.MaxAmount)
            throw new BudgetException(BudgetError.AmountTooLarge, $"Amount {amount} is too large.");
    }

    private static string? Normalize(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class BalanceRow
    {
        public int PocketId { get; set; }
        public long BalanceCents { get; set; }
    }
}
