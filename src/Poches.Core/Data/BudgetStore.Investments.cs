using Poches.Core.Import;
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
        await NormalizeAsync(holding);
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

    /// <summary>Deletes a holding with its purchases, sales and dividends.</summary>
    public async Task DeleteHoldingAsync(int holdingId)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM purchases WHERE HoldingId = ?", holdingId);
            conn.Execute("DELETE FROM sales WHERE HoldingId = ?", holdingId);
            conn.Execute("DELETE FROM dividends WHERE HoldingId = ?", holdingId);
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
        if (purchase.TobCents < 0 || purchase.Tob > Money.MaxAmount)
            throw new BudgetException(BudgetError.InvalidTob, $"Invalid stock-exchange tax {purchase.Tob}.");
        NormalizeCurrency(purchase);
        if (purchase.FromDividendsCents is <= 0)
            purchase.FromDividendsCents = null;
        if (purchase.FromDividends > purchase.Invested + 0.01m)
            throw new BudgetException(BudgetError.InvalidFromDividends, "The part paid with dividends exceeds the purchase.");
        if (newHolding is not null)
            await NormalizeAsync(newHolding);

        var db = await GetConnectionAsync();
        if (newHolding is null && await db.FindAsync<Holding>(purchase.HoldingId) is null)
            throw new BudgetException(BudgetError.HoldingNotFound, $"Holding {purchase.HoldingId} does not exist.");
        if (purchase.Id != 0 && newHolding is null)
        {
            // Moving or shrinking a purchase must not leave a sale without the units it sold.
            var purchases = (await db.Table<Purchase>().ToListAsync()).Select(p => p.Id == purchase.Id ? purchase : p).ToList();
            if (InvestmentMath.Oversold(purchases, await db.Table<Sale>().ToListAsync()))
                throw new BudgetException(BudgetError.PurchaseUsedBySale, "A sale uses units of this purchase.");
        }

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
        var purchases = (await db.Table<Purchase>().ToListAsync()).Where(p => p.Id != purchaseId).ToList();
        if (InvestmentMath.Oversold(purchases, await db.Table<Sale>().ToListAsync()))
            throw new BudgetException(BudgetError.PurchaseUsedBySale, "A sale uses units of this purchase.");
        if (await db.DeleteAsync<Purchase>(purchaseId) == 0)
            throw new BudgetException(BudgetError.PurchaseNotFound, $"Purchase {purchaseId} does not exist.");
        OnChanged();
    }

    public async Task<IReadOnlyList<Sale>> GetSalesAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Sale>().OrderBy(s => s.Date).ThenBy(s => s.Id).ToListAsync();
    }

    public async Task<Sale?> GetSaleAsync(int saleId) => await (await GetConnectionAsync()).FindAsync<Sale>(saleId);

    /// <summary>Saves a sale; it can't sell more units than were bought by its date.</summary>
    public async Task<Sale> SaveSaleAsync(Sale sale, DateTime today)
    {
        sale.Date = sale.Date.Date;
        if (sale.QuantityMicros <= 0 || sale.Quantity > Units.MaxQuantity)
            throw new BudgetException(BudgetError.InvalidQuantity, $"Invalid quantity {sale.Quantity}.");
        if (sale.UnitPriceMicros <= 0 || sale.UnitPrice > Money.MaxAmount)
            throw new BudgetException(BudgetError.InvalidUnitPrice, $"Invalid unit price {sale.UnitPrice}.");
        if (sale.FeesCents < 0 || sale.Fees > Money.MaxAmount)
            throw new BudgetException(BudgetError.InvalidFees, $"Invalid fees {sale.Fees}.");
        if (sale.TobCents < 0 || sale.Tob > Money.MaxAmount)
            throw new BudgetException(BudgetError.InvalidTob, $"Invalid stock-exchange tax {sale.Tob}.");
        if (sale.Date > today.Date)
            throw new BudgetException(BudgetError.SaleInFuture, $"Sale dated {sale.Date:d} is in the future.");
        NormalizeCurrency(sale);

        var db = await GetConnectionAsync();
        if (await db.FindAsync<Holding>(sale.HoldingId) is null)
            throw new BudgetException(BudgetError.HoldingNotFound, $"Holding {sale.HoldingId} does not exist.");
        var sales = (await db.Table<Sale>().ToListAsync()).Where(s => s.Id != sale.Id).Append(sale).ToList();
        if (InvestmentMath.Oversold(await db.Table<Purchase>().ToListAsync(), sales))
            throw new BudgetException(BudgetError.SaleExceedsHoldings, "The sale exceeds the units held on its date.");

        if (sale.Id == 0) await db.InsertAsync(sale);
        else await db.UpdateAsync(sale);
        OnChanged();
        return sale;
    }

    public async Task DeleteSaleAsync(int saleId)
    {
        var db = await GetConnectionAsync();
        if (await db.DeleteAsync<Sale>(saleId) == 0)
            throw new BudgetException(BudgetError.SaleNotFound, $"Sale {saleId} does not exist.");
        OnChanged();
    }

    public async Task<IReadOnlyList<Dividend>> GetDividendsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Dividend>().OrderBy(d => d.Date).ThenBy(d => d.Id).ToListAsync();
    }

    public async Task<Dividend?> GetDividendAsync(int dividendId) => await (await GetConnectionAsync()).FindAsync<Dividend>(dividendId);

    public async Task<Dividend> SaveDividendAsync(Dividend dividend, DateTime today) =>
        (await SaveDividendsAsync([dividend], today))[0];

    /// <summary>Saves dividends together (all or none), such as those detected from the quotes.</summary>
    public async Task<IReadOnlyList<Dividend>> SaveDividendsAsync(IReadOnlyList<Dividend> dividends, DateTime today)
    {
        var db = await GetConnectionAsync();
        foreach (var dividend in dividends)
        {
            dividend.Date = dividend.Date.Date;
            if (dividend.GrossCents <= 0 || dividend.Gross > Money.MaxAmount)
                throw new BudgetException(BudgetError.InvalidGross, $"Invalid gross amount {dividend.Gross}.");
            if (dividend.WithholdingCents < 0 || dividend.WithholdingCents > dividend.GrossCents)
                throw new BudgetException(BudgetError.InvalidWithholding, $"Invalid withholding {dividend.Withholding}.");
            if (dividend.Date > today.Date)
                throw new BudgetException(BudgetError.PurchaseInFuture, $"Dividend dated {dividend.Date:d} is in the future.");
            NormalizeCurrency(dividend);
            if (await db.FindAsync<Holding>(dividend.HoldingId) is null)
                throw new BudgetException(BudgetError.HoldingNotFound, $"Holding {dividend.HoldingId} does not exist.");
        }
        await db.RunInTransactionAsync(conn =>
        {
            foreach (var dividend in dividends)
            {
                if (dividend.Id == 0) conn.Insert(dividend);
                else conn.Update(dividend);
            }
        });
        OnChanged();
        return dividends;
    }

    public async Task DeleteDividendAsync(int dividendId)
    {
        var db = await GetConnectionAsync();
        if (await db.DeleteAsync<Dividend>(dividendId) == 0)
            throw new BudgetException(BudgetError.DividendNotFound, $"Dividend {dividendId} does not exist.");
        OnChanged();
    }

    /// <summary>
    /// Records the ready rows of a statement import with the lines it creates, all or none. A purchase or sale without
    /// TOB in the file gets the line's rate when <paramref name="computeTob"/> (the Belgian option) is on.
    /// </summary>
    /// <returns>How many operations were recorded.</returns>
    public async Task<int> ImportAsync(CsvImportPlan plan, TaxEnvelope envelope, Func<int, string> colorFor, bool computeTob, DateTime today)
    {
        var db = await GetConnectionAsync();
        var existing = await db.Table<Holding>().ToListAsync();
        var newHoldings = plan.NewHoldings.Select((h, i) => (h.Key, Holding: new Holding
        {
            Name = h.Name,
            Code = h.Isin,
            Type = h.Type,
            Envelope = envelope,
            ColorHex = colorFor(existing.Count + i),
        })).ToDictionary(x => x.Key, x => x.Holding);
        foreach (var holding in newHoldings.Values)
            Normalize(holding);
        var byId = existing.ToDictionary(h => h.Id);

        var purchases = new List<(Purchase Purchase, string? NewKey)>();
        var sales = new List<(Sale Sale, string? NewKey)>();
        var dividends = new List<(Dividend Dividend, string? NewKey)>();
        foreach (var row in plan.Ready)
        {
            var op = row.Operation;
            var holding = row.HoldingId is { } id ? byId.GetValueOrDefault(id) : newHoldings.GetValueOrDefault(row.NewHoldingKey ?? string.Empty);
            if (holding is null)
                throw new BudgetException(BudgetError.HoldingNotFound, "The import refers to a missing line.");
            long? rate = row.FxRate is { } fx ? Units.ToMicros(fx) : null;
            var tobRate = computeTob ? BelgianTax.TobRateFor(holding) : 0;
            var tob = op.Tob > 0 ? op.Tob : BelgianTax.Tob(tobRate, op.Qty * op.Price);
            switch (op.Kind)
            {
                case "buy":
                    var purchase = new Purchase { HoldingId = row.HoldingId ?? 0, Date = op.Date!.Value.Date, Quantity = op.Qty, UnitPrice = op.Price, Fees = op.Fees, Tob = tob, Currency = row.Currency, FxRateMicros = rate };
                    ValidateTrade(purchase.QuantityMicros, purchase.Quantity, purchase.UnitPriceMicros, purchase.UnitPrice, purchase.FeesCents, purchase.Fees, purchase.TobCents, purchase.Tob);
                    NormalizeCurrency(purchase);
                    purchases.Add((purchase, row.NewHoldingKey));
                    break;
                case "sell":
                    var sale = new Sale { HoldingId = row.HoldingId ?? 0, Date = op.Date!.Value.Date, Quantity = op.Qty, UnitPrice = op.Price, Fees = op.Fees, Tob = tob, Currency = row.Currency, FxRateMicros = rate };
                    ValidateTrade(sale.QuantityMicros, sale.Quantity, sale.UnitPriceMicros, sale.UnitPrice, sale.FeesCents, sale.Fees, sale.TobCents, sale.Tob);
                    NormalizeCurrency(sale);
                    sales.Add((sale, row.NewHoldingKey));
                    break;
                default:
                    var dividend = new Dividend { HoldingId = row.HoldingId ?? 0, Date = op.Date!.Value.Date, Gross = op.Gross, Withholding = Math.Min(op.Withholding, op.Gross), Currency = row.Currency, FxRateMicros = rate };
                    if (dividend.GrossCents <= 0 || dividend.Gross > Money.MaxAmount)
                        throw new BudgetException(BudgetError.InvalidGross, $"Invalid gross amount {dividend.Gross}.");
                    NormalizeCurrency(dividend);
                    dividends.Add((dividend, row.NewHoldingKey));
                    break;
            }
            if (op.Date!.Value.Date > today.Date)
                throw new BudgetException(BudgetError.PurchaseInFuture, "An imported operation is in the future.");
        }

        // New lines get provisional ids so the oversell check can tell them apart before they exist.
        var provisional = newHoldings.Keys.Select((k, i) => (k, Id: -1 - i)).ToDictionary(x => x.k, x => x.Id);
        int IdOf(int holdingId, string? key) => key is null ? holdingId : provisional[key];
        var allPurchases = (await db.Table<Purchase>().ToListAsync())
            .Concat(purchases.Select(p => Clone(p.Purchase, IdOf(p.Purchase.HoldingId, p.NewKey)))).ToList();
        var allSales = (await db.Table<Sale>().ToListAsync())
            .Concat(sales.Select(s => new Sale { HoldingId = IdOf(s.Sale.HoldingId, s.NewKey), Date = s.Sale.Date, QuantityMicros = s.Sale.QuantityMicros })).ToList();
        if (InvestmentMath.Oversold(allPurchases, allSales))
            throw new BudgetException(BudgetError.SaleExceedsHoldings, "An imported sale exceeds the units bought by its date.");

        await db.RunInTransactionAsync(conn =>
        {
            foreach (var holding in newHoldings.Values)
            {
                holding.CreatedAt = DateTime.Now;
                conn.Insert(holding);
            }
            int Resolve(int holdingId, string? key) => key is null ? holdingId : newHoldings[key].Id;
            foreach (var (purchase, key) in purchases)
            {
                purchase.HoldingId = Resolve(purchase.HoldingId, key);
                conn.Insert(purchase);
            }
            foreach (var (sale, key) in sales)
            {
                sale.HoldingId = Resolve(sale.HoldingId, key);
                conn.Insert(sale);
            }
            foreach (var (dividend, key) in dividends)
            {
                dividend.HoldingId = Resolve(dividend.HoldingId, key);
                conn.Insert(dividend);
            }
        });
        OnChanged();
        return purchases.Count + sales.Count + dividends.Count;

        static Purchase Clone(Purchase p, int holdingId) => new() { HoldingId = holdingId, Date = p.Date, QuantityMicros = p.QuantityMicros, UnitPriceMicros = p.UnitPriceMicros };
    }

    private static void ValidateTrade(long quantityMicros, decimal quantity, long priceMicros, decimal price, long feesCents, decimal fees, long tobCents, decimal tob)
    {
        if (quantityMicros <= 0 || quantity > Units.MaxQuantity)
            throw new BudgetException(BudgetError.InvalidQuantity, $"Invalid quantity {quantity}.");
        if (priceMicros <= 0 || price > Money.MaxAmount)
            throw new BudgetException(BudgetError.InvalidUnitPrice, $"Invalid unit price {price}.");
        if (feesCents < 0 || fees > Money.MaxAmount)
            throw new BudgetException(BudgetError.InvalidFees, $"Invalid fees {fees}.");
        if (tobCents < 0 || tob > Money.MaxAmount)
            throw new BudgetException(BudgetError.InvalidTob, $"Invalid stock-exchange tax {tob}.");
    }

    /// <summary>The plan, goal and tax settings; the defaults until something is saved.</summary>
    public async Task<InvestmentSettings> GetInvestmentSettingsAsync() =>
        await (await GetConnectionAsync()).FindAsync<InvestmentSettings>(InvestmentSettings.SingleId) ?? new InvestmentSettings();

    public async Task SaveInvestmentSettingsAsync(InvestmentSettings settings, DateTime today)
    {
        settings.Id = InvestmentSettings.SingleId;
        if (settings.MonthlyCents is <= 0)
            settings.MonthlyCents = null;
        if (settings.MonthlyCents > Money.ToCents(Money.MaxAmount) || settings.MonthlyDay is < 1 or > 28)
            throw new BudgetException(BudgetError.InvalidPlan, "The monthly plan is invalid.");
        if (settings.GoalCents is <= 0)
            (settings.GoalCents, settings.GoalDate) = (null, null);
        if (settings.GoalCents is not null && (settings.GoalDate is not { } goalDate || goalDate.Date <= today.Date
            || settings.ExpectedReturn is < -0.5m or > 0.5m || settings.Volatility is < 0 or > 1))
            throw new BudgetException(BudgetError.InvalidGoal, "The goal is invalid.");
        if (settings.CapitalGainsRate is < 0 or > 0.5m || settings.ExemptionCents < 0)
            throw new BudgetException(BudgetError.InvalidTaxSettings, "The tax settings are invalid.");
        settings.GoalDate = settings.GoalDate?.Date;
        await (await GetConnectionAsync()).InsertOrReplaceAsync(settings);
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

    // An operation in the portfolio currency keeps neither currency nor rate.
    private static void NormalizeCurrency(IForeignOperation operation)
    {
        var (currency, rate) = operation switch
        {
            Purchase p => (p.Currency, p.FxRateMicros),
            Sale s => (s.Currency, s.FxRateMicros),
            Dividend d => (d.Currency, d.FxRateMicros),
            _ => (null, null),
        };
        currency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
        if (currency is not null && !Operations.Currencies.Contains(currency))
            throw new BudgetException(BudgetError.InvalidCurrency, $"Unknown currency {currency}.");
        if (currency is not null && (rate is not { } micros || micros <= 0 || Units.FromMicros(micros) > Operations.MaxFxRate))
            throw new BudgetException(BudgetError.InvalidFxRate, "The exchange rate is invalid.");
        if (currency is null) rate = null;
        switch (operation)
        {
            case Purchase p: (p.Currency, p.FxRateMicros) = (currency, rate); break;
            case Sale s: (s.Currency, s.FxRateMicros) = (currency, rate); break;
            case Dividend d: (d.Currency, d.FxRateMicros) = (currency, rate); break;
        }
    }

    private async Task NormalizeAsync(Holding holding)
    {
        Normalize(holding);
        if (holding.TargetMicros is { } target)
        {
            var others = (await (await GetConnectionAsync()).Table<Holding>().ToListAsync()).Where(h => h.Id != holding.Id).Sum(h => h.TargetMicros ?? 0);
            if (others + target > 1_000_100)
                throw new BudgetException(BudgetError.TargetsOverHundredPercent, "The targets exceed 100 %.");
        }
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
        if (!Enum.IsDefined(holding.Envelope))
            holding.Envelope = TaxEnvelope.Standard;
        if (holding.TobRate is { } tob && tob != 0 && !BelgianTax.TobCaps.ContainsKey(tob))
            throw new BudgetException(BudgetError.InvalidTobRate, $"Unknown stock-exchange tax rate {tob}.");
        if (holding.Ter is < 0 or > 0.05m)
            throw new BudgetException(BudgetError.InvalidTer, $"Invalid running costs {holding.Ter}.");
        if (holding.TargetMicros is 0)
            holding.TargetMicros = null;
        if (holding.Target is < 0 or > 1)
            throw new BudgetException(BudgetError.InvalidTarget, $"Invalid target {holding.Target}.");
        if (holding.ReferencePriceMicros is <= 0 && holding.ReferencePriceMicros is not null)
            throw new BudgetException(BudgetError.InvalidReferencePrice, "Invalid reference price.");
    }
}
