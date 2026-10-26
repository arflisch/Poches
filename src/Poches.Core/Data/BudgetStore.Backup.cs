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
        var schedules = await db.Table<ScheduledDeposit>().OrderBy(s => s.Id).ToListAsync();
        var splitRules = await db.Table<SplitRule>().ToListAsync();
        var holdings = await db.Table<Holding>().OrderBy(h => h.Id).ToListAsync();
        var purchases = await db.Table<Purchase>().OrderBy(p => p.Date).ThenBy(p => p.Id).ToListAsync();
        var sales = await db.Table<Sale>().OrderBy(s => s.Date).ThenBy(s => s.Id).ToListAsync();
        var dividends = await db.Table<Dividend>().OrderBy(d => d.Date).ThenBy(d => d.Id).ToListAsync();
        var settings = await db.FindAsync<InvestmentSettings>(InvestmentSettings.SingleId);

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
                .Select(s => new BackupSubscription(s.Name, s.Icon, s.ColorHex, s.AmountCents, s.Period, s.BillingAnchor, s.IsActive, s.CreatedAt, s.Category))
                .ToList(),
            ScheduledDeposits = schedules
                .Select(s => new BackupScheduledDeposit(s.PocketId, s.AmountCents, s.Period, s.Anchor, s.AppliedThrough, s.Note, s.IsActive, s.CreatedAt))
                .ToList(),
            SplitRules = splitRules.Select(r => new BackupSplitRule(r.PocketId, r.BasisPoints)).ToList(),
            Holdings = holdings
                .Select(h => new BackupHolding(h.Id, h.Name, h.Code, h.Symbol, h.Type, h.Account, h.ColorHex, h.ManualPriceMicros, h.ManualPriceDate, h.CreatedAt,
                    h.Envelope, h.TobRateMicros, h.TerMicros, h.TargetMicros, h.ReferencePriceMicros, h.Reynders))
                .ToList(),
            Purchases = purchases
                .Select(p => new BackupPurchase(p.HoldingId, p.Date, p.QuantityMicros, p.UnitPriceMicros, p.FeesCents, p.TobCents, p.Currency, p.FxRateMicros, p.FromDividendsCents))
                .ToList(),
            Sales = sales
                .Select(s => new BackupSale(s.HoldingId, s.Date, s.QuantityMicros, s.UnitPriceMicros, s.FeesCents, s.TobCents, s.Currency, s.FxRateMicros))
                .ToList(),
            Dividends = dividends
                .Select(d => new BackupDividend(d.HoldingId, d.Date, d.GrossCents, d.WithholdingCents, d.Currency, d.FxRateMicros))
                .ToList(),
            InvestmentSettings = settings is null ? null : new BackupInvestmentSettings(settings.MonthlyCents, settings.MonthlyDay, settings.GoalCents,
                settings.GoalDate, settings.ExpectedReturnMicros, settings.VolatilityMicros, settings.CapitalGainsRateMicros, settings.ExemptionCents,
                settings.UsShortTermRateMicros, settings.UsLongTermRateMicros, settings.UsNiitRateMicros, settings.UsStateRateMicros, settings.UsLossLimitCents),
        };
    }

    /// <summary>
    /// Replaces all current data with the content of <paramref name="backup"/>, atomically. Backups made before
    /// subscriptions existed (version 1) leave the current subscriptions untouched, and those made before
    /// investments existed (before version 5) leave the current investments untouched. Scheduled deposits and split
    /// rules always follow the backup, since they point to pockets that are replaced.
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
            conn.DeleteAll<ScheduledDeposit>();
            conn.DeleteAll<SplitRule>();

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

            foreach (var s in backup.ScheduledDeposits)
            {
                conn.Insert(new ScheduledDeposit
                {
                    PocketId = newIds[s.PocketId],
                    AmountCents = s.AmountCents,
                    Period = s.Period,
                    Anchor = s.Anchor.Date,
                    AppliedThrough = s.AppliedThrough.Date,
                    Note = s.Note,
                    IsActive = s.IsActive,
                    CreatedAt = s.CreatedAt,
                });
            }
            foreach (var r in backup.SplitRules)
                conn.Insert(new SplitRule { PocketId = newIds[r.PocketId], BasisPoints = r.BasisPoints });

            if (backup.Version >= 5)
            {
                conn.DeleteAll<Purchase>();
                conn.DeleteAll<Sale>();
                conn.DeleteAll<Dividend>();
                conn.DeleteAll<Holding>();
                var holdingIds = new Dictionary<int, int>();
                foreach (var h in backup.Holdings)
                {
                    var holding = new Holding
                    {
                        Name = h.Name.Trim(),
                        Code = h.Code,
                        Symbol = h.Symbol is { } symbol && Investments.Symbols.IsValid(symbol) ? symbol.ToUpperInvariant() : null,
                        Type = h.Type,
                        Account = h.Account,
                        ColorHex = string.IsNullOrWhiteSpace(h.ColorHex) ? "#6366F1" : h.ColorHex,
                        ManualPriceMicros = h.ManualPriceMicros is > 0 ? h.ManualPriceMicros : null,
                        ManualPriceDate = h.ManualPriceMicros is > 0 ? h.ManualPriceDate : null,
                        CreatedAt = h.CreatedAt,
                        Envelope = Enum.IsDefined(h.Envelope) ? h.Envelope : TaxEnvelope.Standard,
                        TobRateMicros = h.TobRateMicros,
                        TerMicros = h.TerMicros,
                        TargetMicros = h.TargetMicros is > 0 ? h.TargetMicros : null,
                        ReferencePriceMicros = h.ReferencePriceMicros is > 0 ? h.ReferencePriceMicros : null,
                        Reynders = h.Reynders,
                    };
                    conn.Insert(holding);
                    holdingIds[h.Id] = holding.Id;
                }
                foreach (var p in backup.Purchases)
                {
                    conn.Insert(new Purchase
                    {
                        HoldingId = holdingIds[p.HoldingId],
                        Date = p.Date.Date,
                        QuantityMicros = p.QuantityMicros,
                        UnitPriceMicros = p.UnitPriceMicros,
                        FeesCents = p.FeesCents,
                        TobCents = p.TobCents,
                        Currency = p.Currency,
                        FxRateMicros = p.Currency is null ? null : p.FxRateMicros,
                        FromDividendsCents = p.FromDividendsCents is > 0 ? p.FromDividendsCents : null,
                    });
                }
                foreach (var s in backup.Sales)
                {
                    conn.Insert(new Sale
                    {
                        HoldingId = holdingIds[s.HoldingId],
                        Date = s.Date.Date,
                        QuantityMicros = s.QuantityMicros,
                        UnitPriceMicros = s.UnitPriceMicros,
                        FeesCents = s.FeesCents,
                        TobCents = s.TobCents,
                        Currency = s.Currency,
                        FxRateMicros = s.Currency is null ? null : s.FxRateMicros,
                    });
                }
                foreach (var d in backup.Dividends)
                {
                    conn.Insert(new Dividend
                    {
                        HoldingId = holdingIds[d.HoldingId],
                        Date = d.Date.Date,
                        GrossCents = d.GrossCents,
                        WithholdingCents = d.WithholdingCents,
                        Currency = d.Currency,
                        FxRateMicros = d.Currency is null ? null : d.FxRateMicros,
                    });
                }
            }

            // The plan and goal follow the backup from version 6; older ones leave them as they are.
            if (backup.Version >= 6)
            {
                conn.DeleteAll<InvestmentSettings>();
                if (backup.InvestmentSettings is { } s)
                {
                    conn.Insert(new InvestmentSettings
                    {
                        MonthlyCents = s.MonthlyCents is > 0 ? s.MonthlyCents : null,
                        MonthlyDay = s.MonthlyDay is >= 1 and <= 28 ? s.MonthlyDay : 5,
                        GoalCents = s.GoalCents is > 0 ? s.GoalCents : null,
                        GoalDate = s.GoalCents is > 0 ? s.GoalDate : null,
                        ExpectedReturnMicros = s.ExpectedReturnMicros,
                        VolatilityMicros = s.VolatilityMicros,
                        CapitalGainsRateMicros = s.CapitalGainsRateMicros,
                        ExemptionCents = s.ExemptionCents,
                        // Backups made before the US rules keep their defaults.
                        UsShortTermRateMicros = s.UsShortTermRateMicros ?? 220_000,
                        UsLongTermRateMicros = s.UsLongTermRateMicros ?? 150_000,
                        UsNiitRateMicros = s.UsNiitRateMicros ?? 0,
                        UsStateRateMicros = s.UsStateRateMicros ?? 0,
                        UsLossLimitCents = s.UsLossLimitCents ?? 300_000,
                    });
                }
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
                        Category = s.Category,
                        BillingAnchor = s.BillingAnchor.Date,
                        IsActive = s.IsActive,
                        CreatedAt = s.CreatedAt,
                    });
                }
            }
        });

        OnChanged();
    }

    /// <summary>
    /// Deletes every pocket, movement, subscription, scheduled deposit, split rule and investment, with the market
    /// data fetched for them (it would still tell which securities were held).
    /// </summary>
    public async Task DeleteAllAsync()
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<Movement>();
            conn.DeleteAll<Pocket>();
            conn.DeleteAll<Subscription>();
            conn.DeleteAll<ScheduledDeposit>();
            conn.DeleteAll<SplitRule>();
            conn.DeleteAll<Purchase>();
            conn.DeleteAll<Sale>();
            conn.DeleteAll<Dividend>();
            conn.DeleteAll<InvestmentSettings>();
            conn.DeleteAll<Holding>();
            conn.DeleteAll<CachedQuote>();
            conn.DeleteAll<PriceClose>();
            conn.DeleteAll<PriceCoverage>();
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

        if (backup.Subscriptions.Any(s => string.IsNullOrWhiteSpace(s.Name) || s.AmountCents <= 0 || !Enum.IsDefined(s.Period) || !Enum.IsDefined(s.Category)))
            throw new BudgetException(BudgetError.CorruptBackup, "A subscription is incomplete.");

        if (backup.ScheduledDeposits.Any(s => !ids.Contains(s.PocketId) || s.AmountCents <= 0 || !Enum.IsDefined(s.Period)))
            throw new BudgetException(BudgetError.CorruptBackup, "A scheduled deposit is incomplete.");

        if (backup.SplitRules.Any(r => !ids.Contains(r.PocketId) || r.BasisPoints <= 0)
            || backup.SplitRules.Sum(r => r.BasisPoints) > Pro.SalarySplit.WholeIncome
            || backup.SplitRules.Select(r => r.PocketId).Distinct().Count() != backup.SplitRules.Count)
            throw new BudgetException(BudgetError.CorruptBackup, "The split rules are inconsistent.");

        var holdingIds = new HashSet<int>();
        foreach (var holding in backup.Holdings)
        {
            if (!holdingIds.Add(holding.Id) || string.IsNullOrWhiteSpace(holding.Name) || !Enum.IsDefined(holding.Type))
                throw new BudgetException(BudgetError.CorruptBackup, $"Invalid or duplicate holding {holding.Id}.");
        }
        if (backup.Purchases.Any(p => !holdingIds.Contains(p.HoldingId) || p.QuantityMicros <= 0 || p.UnitPriceMicros <= 0 || p.FeesCents < 0
                || p.TobCents < 0 || !ValidCurrency(p.Currency, p.FxRateMicros)))
            throw new BudgetException(BudgetError.CorruptBackup, "A purchase is incomplete or references an unknown holding.");
        if (backup.Sales.Any(s => !holdingIds.Contains(s.HoldingId) || s.QuantityMicros <= 0 || s.UnitPriceMicros <= 0 || s.FeesCents < 0
                || s.TobCents < 0 || !ValidCurrency(s.Currency, s.FxRateMicros)))
            throw new BudgetException(BudgetError.CorruptBackup, "A sale is incomplete or references an unknown holding.");
        if (backup.Dividends.Any(d => !holdingIds.Contains(d.HoldingId) || d.GrossCents <= 0 || d.WithholdingCents < 0 || d.WithholdingCents > d.GrossCents
                || !ValidCurrency(d.Currency, d.FxRateMicros)))
            throw new BudgetException(BudgetError.CorruptBackup, "A dividend is incomplete or references an unknown holding.");
        // Sales can't sell more than was bought: check with the backup's own ids.
        var purchases = backup.Purchases.Select(p => new Purchase { HoldingId = p.HoldingId, Date = p.Date, QuantityMicros = p.QuantityMicros }).ToList();
        var sales = backup.Sales.Select(s => new Sale { HoldingId = s.HoldingId, Date = s.Date, QuantityMicros = s.QuantityMicros }).ToList();
        if (Investments.InvestmentMath.Oversold(purchases, sales))
            throw new BudgetException(BudgetError.CorruptBackup, "A sale exceeds the units bought.");
    }

    private static bool ValidCurrency(string? currency, long? rateMicros) =>
        currency is null || (Operations.Currencies.Contains(currency) && rateMicros is > 0);
}
