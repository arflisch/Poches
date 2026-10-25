using System.Text.RegularExpressions;
using Poches.Core.Investments;
using Poches.Core.Models;

namespace Poches.Core.Import;

public enum CsvRowStatus
{
    Ready,
    Duplicate,
    Error,
}

/// <summary>A line the import creates, for operations whose instrument isn't in the portfolio yet.</summary>
public sealed record CsvNewHolding(string Key, string Name, string? Isin, HoldingType Type);

/// <summary>
/// One row of the file and what the import does with it. <see cref="Error"/> is a code: the reading errors of
/// <see cref="BankCsv"/>, or currency, rate, future, unknown_line. <see cref="Currency"/> and <see cref="FxRate"/> are set
/// for a foreign operation (units of the currency for one unit of the portfolio currency).
/// </summary>
public sealed record CsvPlannedRow(
    CsvOperation Operation,
    CsvRowStatus Status,
    string? Error,
    int? HoldingId,
    string? NewHoldingKey,
    string LineName,
    string? Currency,
    decimal? FxRate,
    bool RateFromMarket);

public sealed record CsvImportPlan(IReadOnlyList<CsvPlannedRow> Rows, IReadOnlyList<CsvNewHolding> NewHoldings)
{
    public int Purchases => Ready.Count(r => r.Operation.Kind == "buy");

    public int Sales => Ready.Count(r => r.Operation.Kind == "sell");

    public int Dividends => Ready.Count(r => r.Operation.Kind == "dividend");

    public int Duplicates => Rows.Count(r => r.Status == CsvRowStatus.Duplicate);

    public int Errors => Rows.Count(r => r.Status == CsvRowStatus.Error);

    public int Foreign => Ready.Count(r => r.Currency is not null);

    public IEnumerable<CsvPlannedRow> Ready => Rows.Where(r => r.Status == CsvRowStatus.Ready);
}

/// <summary>
/// Matches the operations of a bank statement with the portfolio: the line of each one (same ISIN, then symbol, then
/// name; a purchase of an unknown instrument creates a line), the exchange rate of foreign ones, and the rows already
/// recorded. Same rules as the JavaScript page.
/// </summary>
public static partial class CsvImportPlanner
{
    /// <param name="marketRates">Daily rates by currency (units for one portfolio currency); a null entry when they couldn't be fetched.</param>
    public static CsvImportPlan Build(
        IReadOnlyList<CsvOperation> operations,
        IReadOnlyList<Holding> holdings,
        IReadOnlyList<Purchase> purchases,
        IReadOnlyList<Sale> sales,
        IReadOnlyList<Dividend> dividends,
        string portfolioCurrency,
        IReadOnlyDictionary<string, IReadOnlyList<DailyClose>?> marketRates,
        DateTime today)
    {
        var rows = new List<CsvPlannedRow>();
        var created = new Dictionary<string, CsvNewHolding>();
        foreach (var op in operations)
        {
            var name = op.Name.Length > 0 ? op.Name : op.Isin.Length > 0 ? op.Isin : op.Symbol;
            CsvPlannedRow Fail(string code) => new(op, CsvRowStatus.Error, code, null, null, name, null, null, false);
            if (op.Error is { } error)
            {
                rows.Add(Fail(error));
                continue;
            }
            if (op.Date!.Value.Date > today.Date)
            {
                rows.Add(Fail("future"));
                continue;
            }

            string? currency = null;
            decimal? rate = null;
            var fromMarket = false;
            if (op.Currency.Length > 0 && op.Currency != portfolioCurrency && op.Currency.Length == 3 && op.Currency.All(char.IsAsciiLetterUpper))
            {
                if (!Operations.Currencies.Contains(op.Currency))
                {
                    rows.Add(Fail("currency"));
                    continue;
                }
                var market = FxTools.RateOn(marketRates.GetValueOrDefault(op.Currency), op.Date.Value);
                rate = op.FxRate is { } fileRate ? FxTools.OrientRate(fileRate, market) : market;
                fromMarket = op.FxRate is null;
                if (rate is not > 0 || rate > Operations.MaxFxRate)
                {
                    rows.Add(Fail("rate"));
                    continue;
                }
                currency = op.Currency;
            }

            var holding = Match(op, holdings);
            var key = op.Isin.Length > 0 ? op.Isin : Norm(op.Name) is { Length: > 0 } n ? n : op.Symbol.ToUpperInvariant();
            if (holding is null && !created.ContainsKey(key))
            {
                if (op.Kind != "buy")
                {
                    rows.Add(Fail("unknown_line"));
                    continue;
                }
                created[key] = new CsvNewHolding(key, name.Length > 80 ? name[..80] : name, op.Isin.Length > 0 ? op.Isin : null,
                    FundWords().IsMatch(op.Name) ? HoldingType.Etf : HoldingType.Stock);
            }

            var lineName = holding?.Name ?? created[key].Name;
            var duplicate = holding is not null && IsDuplicate(op, holding.Id, currency, rate, purchases, sales, dividends);
            rows.Add(new CsvPlannedRow(op, duplicate ? CsvRowStatus.Duplicate : CsvRowStatus.Ready, null, holding?.Id,
                holding is null ? key : null, lineName, currency, rate, fromMarket));
        }
        return new CsvImportPlan(rows, created.Values.ToList());
    }

    private static Holding? Match(CsvOperation op, IReadOnlyList<Holding> holdings) =>
        (op.Isin.Length > 0 ? holdings.FirstOrDefault(h => string.Equals(h.Code, op.Isin, StringComparison.OrdinalIgnoreCase)) : null)
        ?? (op.Symbol.Length > 0 ? holdings.FirstOrDefault(h => string.Equals(h.Symbol, op.Symbol, StringComparison.OrdinalIgnoreCase)) : null)
        ?? (op.Name.Length > 0 ? holdings.FirstOrDefault(h => Norm(h.Name) == Norm(op.Name)) : null);

    /// <summary>Already recorded: same line, day and quantity (and price or gross amount, in the operation's currency when both are in it).</summary>
    private static bool IsDuplicate(CsvOperation op, int holdingId, string? currency, decimal? rate,
        IReadOnlyList<Purchase> purchases, IReadOnlyList<Sale> sales, IReadOnlyList<Dividend> dividends)
    {
        static bool Same(decimal x, decimal y) => Math.Abs(x - y) <= Math.Max(0.000001m, Math.Abs(y) * 0.0001m);
        // Compared in the operation's own currency when the recorded one is in it too: the rates may differ.
        bool SameAmount(IForeignOperation recorded, decimal recordedNative, decimal recordedBase, decimal fileNative) =>
            recorded.Currency is not null && recorded.Currency == currency
                ? Same(recordedNative, fileNative)
                : Same(recordedBase, fileNative / (rate ?? 1));
        var date = op.Date!.Value.Date;
        return op.Kind switch
        {
            "buy" => purchases.Any(p => p.HoldingId == holdingId && p.Date.Date == date && Same(p.Quantity, op.Qty) && SameAmount(p, p.UnitPrice, p.BaseUnitPrice, op.Price)),
            "sell" => sales.Any(s => s.HoldingId == holdingId && s.Date.Date == date && Same(s.Quantity, op.Qty)),
            _ => dividends.Any(d => d.HoldingId == holdingId && d.Date.Date == date && SameAmount(d, d.Gross, d.BaseGross, op.Gross)),
        };
    }

    private static string Norm(string text) => BankCsv.Norm(text);

    [GeneratedRegex(@"\b(etf|ucits|ishares|vanguard|amundi|xtrackers|spdr|lyxor|invesco|wisdomtree|vaneck)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FundWords();
}
