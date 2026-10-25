using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Poches.Core.Import;

/// <summary>
/// One row of a bank statement read as an operation. <see cref="Kind"/> is buy, sell or dividend; amounts are in
/// <see cref="Currency"/> when the file has one. A row that can't be read keeps an <see cref="Error"/> code (date, kind, name, gross, qty, price) for the preview.
/// </summary>
public sealed record CsvOperation(
    int Line,
    string? Kind,
    DateTime? Date,
    string Name,
    string Isin,
    string Symbol,
    decimal Qty,
    decimal Price,
    decimal Fees,
    decimal Tob,
    string Currency,
    decimal? FxRate,
    decimal Gross = 0,
    decimal Withholding = 0,
    string? Error = null);

/// <summary>Reads bank CSV exports (Keytrade and most Belgian banks): same rules as the JavaScript page.</summary>
public static partial class BankCsv
{
    /// <summary>Header words for each field, French, Dutch and English. The rate comes before the price so "Cours de change" isn't taken for it.</summary>
    public static readonly IReadOnlyList<(string Field, string[] Words)> Fields =
    [
        ("date", ["date", "datum", "date d'exécution", "date execution", "transactiedatum", "date opération", "trade date"]),
        ("kind", ["type", "opération", "operation", "transaction", "sens", "action", "soort", "type de transaction", "buy/sell"]),
        ("name", ["produit", "product", "description", "libellé", "libelle", "titre", "instrument", "naam", "nom", "security", "omschrijving"]),
        ("isin", ["isin", "code isin", "isin code"]),
        ("symbol", ["symbole", "symbol", "ticker", "code"]),
        ("fx", ["taux de change", "cours de change", "taux", "change", "exchange rate", "fx rate", "fx", "wisselkoers"]),
        ("qty", ["quantité", "quantite", "qty", "quantity", "aantal", "nombre", "nombre de titres", "shares"]),
        ("price", ["cours", "prix", "price", "koers", "prix unitaire", "cours unitaire", "unit price"]),
        ("fees", ["frais", "courtage", "fees", "commission", "kosten", "frais de courtage", "makelaarsloon"]),
        ("tob", ["tob", "taxe", "taxes", "tax", "beurstaks", "taxe boursière", "taxe sur les opérations de bourse"]),
        ("amount", ["montant", "amount", "bedrag", "montant net", "net amount", "total"]),
        ("withholding", ["précompte", "precompte", "retenue", "roerende voorheffing", "withholding", "withholding tax"]),
        ("currency", ["devise", "currency", "munt", "valuta"]),
    ];

    /// <summary>Splits CSV text into rows, guessing the separator (; , tab) and honouring quotes.</summary>
    public static (char Separator, List<List<string>> Rows) Parse(string? text)
    {
        var src = (text ?? "").TrimStart('﻿');
        var firstLine = src.Split('\n', 2)[0].TrimEnd('\r');
        var sep = new[] { ';', ',', '\t' }.Select(c => (c, n: firstLine.Count(x => x == c))).OrderByDescending(x => x.n).First() is var best && best.n > 0 ? best.c : ';';
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        void EndRow()
        {
            row.Add(cell.ToString().Trim()); cell.Clear();
            if (row.Any(x => x.Length > 0)) rows.Add(row);
            row = [];
        }
        for (var i = 0; i < src.Length; i++)
        {
            var c = src[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < src.Length && src[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == sep) { row.Add(cell.ToString().Trim()); cell.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < src.Length && src[i + 1] == '\n') i++;
                EndRow();
            }
            else cell.Append(c);
        }
        EndRow();
        return (sep, rows);
    }

    /// <summary>Guesses which column holds which field from the header names.</summary>
    public static Dictionary<string, int> GuessMapping(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>();
        var used = new HashSet<int>();
        var names = headers.Select(Norm).ToList();
        foreach (var (field, words) in Fields)
        {
            var wanted = words.Select(Norm).ToList();
            var idx = Enumerable.Range(0, names.Count).FirstOrDefault(i => !used.Contains(i) && wanted.Contains(names[i]), -1);
            if (idx < 0) idx = Enumerable.Range(0, names.Count).FirstOrDefault(i => !used.Contains(i) && wanted.Any(w => w.Length > 3 && names[i].Contains(w, StringComparison.Ordinal)), -1);
            if (idx >= 0) { map[field] = idx; used.Add(idx); }
        }
        return map;
    }

    /// <summary>"1.234,56", "1 234,56", "1,234.56", "-12", "(5,00)" → number; null when not a number.</summary>
    public static decimal? ParseAmount(string? text)
    {
        var t = CurrencyWords().Replace(new string((text ?? "").Trim().Where(c => !char.IsWhiteSpace(c) && c is not ' ' and not ' ').ToArray()), "");
        if (t.Length == 0) return null;
        var negative = t.Length >= 2 && t[0] == '(' && t[^1] == ')';
        if (negative) t = t[1..^1];
        int lastComma = t.LastIndexOf(','), lastDot = t.LastIndexOf('.');
        if (lastComma > lastDot) t = ReplaceFirst(t.Replace(".", ""), ',', '.');
        else if (lastDot > lastComma && lastComma >= 0) t = t.Replace(",", "");
        else if (lastComma >= 0 && lastDot < 0) t = ReplaceFirst(t, ',', '.');
        if (!NumberPattern().IsMatch(t)) return null;
        var v = decimal.Parse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return negative ? -v : v;
    }

    /// <summary>"31/12/2025", "31-12-2025", "31.12.2025", "2025-12-31", "2025-12-31 10:00" → a date; null otherwise.</summary>
    public static DateTime? ParseDate(string? text)
    {
        var t = (text ?? "").Trim();
        int y, mo, d;
        if (IsoDate().Match(t) is { Success: true } iso)
            (y, mo, d) = (int.Parse(iso.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(iso.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(iso.Groups[3].Value, CultureInfo.InvariantCulture));
        else if (EuropeanDate().Match(t) is { Success: true } eu)
        {
            (d, mo) = (int.Parse(eu.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(eu.Groups[2].Value, CultureInfo.InvariantCulture));
            var ys = eu.Groups[3].Value;
            y = int.Parse(ys.Length == 2 ? "20" + ys : ys, CultureInfo.InvariantCulture);
        }
        else return null;
        return mo is >= 1 and <= 12 && d >= 1 && y is >= 1 and <= 9999 && d <= DateTime.DaysInMonth(y, mo) ? new DateTime(y, mo, d) : null;
    }

    private static readonly string[] Sell = ["vente", "verkoop", "sell", "sale", "v"];
    private static readonly string[] Buy = ["achat", "aankoop", "buy", "purchase", "a", "souscription", "inschrijving"];
    private static readonly string[] Div = ["dividende", "dividend", "coupon", "distribution", "uitkering"];

    private static string? KindOf(string text, decimal? qty)
    {
        var t = Norm(text);
        if (Div.Any(w => t == w || t.Contains(w, StringComparison.Ordinal))) return "dividend";
        if (Sell.Any(w => t == w || (w.Length > 1 && t.Contains(w, StringComparison.Ordinal)))) return "sell";
        if (Buy.Any(w => t == w || (w.Length > 1 && t.Contains(w, StringComparison.Ordinal)))) return "buy";
        if (qty < 0) return "sell";
        return qty > 0 ? "buy" : null;
    }

    /// <summary>
    /// Turns CSV rows into operations with the given column mapping. Rows that can't be read come back with an error
    /// instead of being dropped, so the preview can show them. <paramref name="amountIsNet"/>: the dividend amount is after withholding.
    /// </summary>
    public static List<CsvOperation> ToOperations(IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyDictionary<string, int> map, bool amountIsNet)
    {
        string Get(IReadOnlyList<string> row, string field) => map.TryGetValue(field, out var i) && i < row.Count ? row[i] : "";
        return rows.Select((row, i) =>
        {
            var date = ParseDate(Get(row, "date"));
            var rawQty = ParseAmount(Get(row, "qty"));
            var kind = KindOf(Get(row, "kind"), rawQty);
            var qty = Math.Abs(rawQty ?? 0);
            var price = Math.Abs(ParseAmount(Get(row, "price")) ?? 0);
            var fees = Math.Abs(ParseAmount(Get(row, "fees")) ?? 0);
            var tob = Math.Abs(ParseAmount(Get(row, "tob")) ?? 0);
            var amount = Math.Abs(ParseAmount(Get(row, "amount")) ?? 0);
            var withholding = Math.Abs(ParseAmount(Get(row, "withholding")) ?? 0);
            var isin = IsinPattern().Match(Get(row, "isin")) is { Success: true } m ? m.Value.ToUpperInvariant() : "";
            var fx = ParseAmount(Get(row, "fx")) is { } f && f != 0 ? Math.Abs(f) : (decimal?)null;
            var op = new CsvOperation(i + 1, kind, date, Get(row, "name"), isin, Get(row, "symbol"), qty, price, fees, tob,
                Get(row, "currency").Trim().ToUpperInvariant(), fx);
            if (date is null) return op with { Error = "date" };
            if (kind is null) return op with { Error = "kind" };
            if (op.Name.Length == 0 && isin.Length == 0 && op.Symbol.Length == 0) return op with { Error = "name" };
            if (kind == "dividend")
            {
                // The bank may give the net amount (after withholding) or the gross one: the mapping says which.
                var gross = amount > 0 ? amount + (amountIsNet ? withholding : 0) : 0;
                return gross > 0 ? op with { Gross = gross, Withholding = Math.Min(withholding, gross) } : op with { Error = "gross" };
            }
            if (qty <= 0) return op with { Error = "qty" };
            var unit = price;
            if (unit <= 0 && amount > 0) unit = Math.Max(0, kind == "buy" ? amount - fees - tob : amount + fees + tob) / qty;
            return unit > 0 ? op with { Price = unit } : op with { Error = "price" };
        }).ToList();
    }

    /// <summary>Lower case, no accents, punctuation turned into spaces: how headers and operation types are compared.</summary>
    public static string Norm(string? text)
    {
        var decomposed = (text ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new string(decomposed.Where(c => c is < '̀' or > 'ͯ').ToArray());
        return NotWordChars().Replace(plain, " ").Trim();
    }

    private static string ReplaceFirst(string text, char from, char to)
    {
        var i = text.IndexOf(from);
        return i < 0 ? text : string.Concat(text.AsSpan(0, i), to.ToString(), text.AsSpan(i + 1));
    }

    [GeneratedRegex("[€$£]|EUR|USD|GBP|CHF", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyWords();

    [GeneratedRegex(@"^[-+]?\d*\.?\d+$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^(\d{4})-(\d{1,2})-(\d{1,2})")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"^(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2,4})")]
    private static partial Regex EuropeanDate();

    [GeneratedRegex("[A-Z]{2}[A-Z0-9]{9}[0-9]", RegexOptions.IgnoreCase)]
    private static partial Regex IsinPattern();

    [GeneratedRegex("[^a-z0-9/' ]+")]
    private static partial Regex NotWordChars();
}
