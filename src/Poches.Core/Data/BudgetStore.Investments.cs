using Poches.Core.Investments;
using Poches.Core.Models;

namespace Poches.Core.Data;

public sealed partial class BudgetStore
{
    public async Task<IReadOnlyList<Holding>> GetHoldingsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Holding>().OrderBy(h => h.Id).ToListAsync();
    }

    public async Task<Holding?> GetHoldingAsync(int holdingId) =>
        await (await GetConnectionAsync()).FindAsync<Holding>(holdingId);

    public async Task<IReadOnlyList<Purchase>> GetPurchasesAsync(int? holdingId = null)
    {
        var db = await GetConnectionAsync();
        var query = db.Table<Purchase>();
        if (holdingId is { } id)
            query = query.Where(p => p.HoldingId == id);
        return await query.OrderBy(p => p.Date).ThenBy(p => p.Id).ToListAsync();
    }

    public async Task<Purchase?> GetPurchaseAsync(int purchaseId) =>
        await (await GetConnectionAsync()).FindAsync<Purchase>(purchaseId);

    public async Task<Holding> SaveHoldingAsync(Holding holding)
    {
        Normalize(holding);
        var db = await GetConnectionAsync();
        if (holding.Id == 0)
        {
            holding.CreatedAt = DateTime.Now;
            await db.InsertAsync(holding);
        }
        else
        {
            await db.UpdateAsync(holding);
        }
        OnChanged();
        return holding;
    }

    /// <summary>Deletes a holding and its purchases.</summary>
    public async Task DeleteHoldingAsync(int holdingId)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM purchases WHERE HoldingId = ?", holdingId);
            conn.Delete<Holding>(holdingId);
        });
        OnChanged();
    }

    /// <summary>
    /// Saves a purchase, creating <paramref name="newHolding"/> first when given (both or neither are saved).
    /// </summary>
    public async Task<Purchase> SavePurchaseAsync(Purchase purchase, DateTime today, Holding? newHolding = null)
    {
        purchase.Date = purchase.Date.Date;
        if (purchase.QuantityMicros <= 0 || purchase.Quantity > Units.MaxQuantity)
            throw new BudgetException(BudgetError.InvalidQuantity, $"Invalid quantity {purchase.Quantity}.");
        if (purchase.UnitPriceMicros <= 0 || purchase.UnitPrice > Money.MaxAmount)
            throw new BudgetException(BudgetError.InvalidUnitPrice, $"Invalid unit price {purchase.UnitPrice}.");
        if (purchase.FeesCents < 0 || purchase.Fees > Money.MaxAmount)
            throw new BudgetException(BudgetError.InvalidFees, $"Invalid fees {purchase.Fees}.");
        if (purchase.Date > today.Date)
            throw new BudgetException(BudgetError.PurchaseInFuture, $"Purchase dated {purchase.Date:d} is in the future.");
        if (newHolding is not null)
            Normalize(newHolding);

        var db = await GetConnectionAsync();
        if (newHolding is null && await db.FindAsync<Holding>(purchase.HoldingId) is null)
            throw new BudgetException(BudgetError.HoldingNotFound, $"Holding {purchase.HoldingId} does not exist.");

        await db.RunInTransactionAsync(conn =>
        {
            if (newHolding is not null)
            {
                newHolding.CreatedAt = DateTime.Now;
                conn.Insert(newHolding);
                purchase.HoldingId = newHolding.Id;
            }
            if (purchase.Id == 0) conn.Insert(purchase);
            else conn.Update(purchase);
        });
        OnChanged();
        return purchase;
    }

    public async Task DeletePurchaseAsync(int purchaseId)
    {
        var db = await GetConnectionAsync();
        if (await db.DeleteAsync<Purchase>(purchaseId) == 0)
            throw new BudgetException(BudgetError.PurchaseNotFound, $"Purchase {purchaseId} does not exist.");
        OnChanged();
    }

    // Market data below is a cache, not the user's data: writing it does not raise Changed, otherwise
    // every refresh would reload the screens that asked for it.

    public async Task<IReadOnlyDictionary<string, CachedQuote>> GetCachedQuotesAsync(IEnumerable<string> symbols)
    {
        var wanted = symbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            return new Dictionary<string, CachedQuote>();
        var db = await GetConnectionAsync();
        var all = await db.Table<CachedQuote>().ToListAsync();
        return all.Where(q => wanted.Contains(q.Symbol)).ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveCachedQuoteAsync(CachedQuote quote)
    {
        var db = await GetConnectionAsync();
        await db.InsertOrReplaceAsync(quote);
    }

    public async Task<PriceCoverage?> GetPriceCoverageAsync(string symbol) =>
        await (await GetConnectionAsync()).FindAsync<PriceCoverage>(symbol);

    public async Task<IReadOnlyList<DailyClose>> GetPriceHistoryAsync(string symbol, DateTime from)
    {
        var db = await GetConnectionAsync();
        var rows = await db.Table<PriceClose>()
            .Where(p => p.Symbol == symbol && p.Date >= from)
            .OrderBy(p => p.Date)
            .ToListAsync();
        return rows.Select(r => new DailyClose(r.Date, (decimal)(float)r.Close)).ToList();
    }

    public async Task SavePriceHistoryAsync(string symbol, string currency, DateTime coveredFrom, DateTime fetchedAt, IReadOnlyList<DailyClose> closes)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            var coverage = conn.Find<PriceCoverage>(symbol);
            conn.InsertOrReplace(new PriceCoverage
            {
                Symbol = symbol,
                Currency = currency,
                CoveredFrom = coverage is not null && coverage.CoveredFrom < coveredFrom ? coverage.CoveredFrom : coveredFrom.Date,
                FetchedAt = fetchedAt,
            });
            foreach (var close in closes)
            {
                conn.Execute(
                    "INSERT INTO price_history (Symbol, Date, Close) VALUES (?, ?, ?) ON CONFLICT (Symbol, Date) DO UPDATE SET Close = excluded.Close",
                    symbol, close.Date.Date, (double)close.Close);
            }
        });
    }

    private static void Normalize(Holding holding)
    {
        holding.Name = holding.Name.Trim();
        if (holding.Name.Length == 0)
            throw new BudgetException(BudgetError.EmptyHoldingName, "A holding needs a name.");
        holding.Code = string.IsNullOrWhiteSpace(holding.Code) ? null : holding.Code.Trim().ToUpperInvariant();
        holding.Account = string.IsNullOrWhiteSpace(holding.Account) ? null : holding.Account.Trim();
        holding.Symbol = string.IsNullOrWhiteSpace(holding.Symbol) ? null : holding.Symbol.Trim().ToUpperInvariant();
        if (holding.Symbol is not null && !Symbols.IsValid(holding.Symbol))
            throw new BudgetException(BudgetError.InvalidSymbol, $"Invalid symbol {holding.Symbol}.");
        if (!Enum.IsDefined(holding.Type))
            holding.Type = HoldingType.Other;
        if (holding.ManualPriceMicros is <= 0)
            holding.ManualPriceMicros = null;
        if (holding.ManualPriceMicros is null)
            holding.ManualPriceDate = null;
        else
            holding.ManualPriceDate = (holding.ManualPriceDate ?? DateTime.Today).Date;
    }
}
