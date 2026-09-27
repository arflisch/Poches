using System.Globalization;
using System.Text;
using Poches.Core.Models;

namespace Poches.Core.Pro;

/// <summary>Column titles and movement labels of the CSV export, in the user's language.</summary>
/// <param name="TransferTo">"Transfer to {0}".</param>
/// <param name="TransferFrom">"Transfer from {0}".</param>
public sealed record CsvTexts(
    string Date, string Pocket, string Type, string Amount, string Note,
    string Deposit, string Withdrawal, string TransferTo, string TransferFrom)
{
    public static CsvTexts French { get; } = new(
        "Date", "Poche", "Type", "Montant", "Note", "Ajout", "Retrait", "Virement vers {0}", "Virement depuis {0}");
}

/// <summary>
/// Writes every movement as a spreadsheet-friendly CSV: the culture's list separator and decimal comma, so
/// Excel or Numbers open it directly, and a BOM so accents survive.
/// </summary>
public static class MovementCsv
{
    public static byte[] Write(
        IReadOnlyList<Pocket> pockets, IEnumerable<Movement> movements, CsvTexts texts, CultureInfo culture)
    {
        var separator = culture.TextInfo.ListSeparator is { Length: > 0 } s ? s : ",";
        var names = pockets.ToDictionary(p => p.Id, p => p.Name);
        string Name(int? id) => id is { } value && names.TryGetValue(value, out var name) ? name : "?";

        var csv = new StringBuilder();
        void Row(params string?[] cells) =>
            csv.Append(string.Join(separator, cells.Select(c => Escape(c ?? string.Empty, separator)))).Append("\r\n");

        Row(texts.Date, texts.Pocket, texts.Type, texts.Amount, texts.Note);
        foreach (var m in movements.OrderBy(m => m.Date).ThenBy(m => m.Id))
        {
            var type = m.Kind switch
            {
                MovementKind.Deposit => texts.Deposit,
                MovementKind.Withdrawal => texts.Withdrawal,
                MovementKind.TransferOut => string.Format(culture, texts.TransferTo, Name(m.CounterpartPocketId)),
                _ => string.Format(culture, texts.TransferFrom, Name(m.CounterpartPocketId)),
            };
            Row(
                m.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Name(m.PocketId),
                type,
                m.Amount.ToString("0.00", culture),
                m.Note);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string Escape(string cell, string separator) =>
        cell.Contains(separator, StringComparison.Ordinal) || cell.IndexOfAny(['"', '\r', '\n']) >= 0
            ? $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : cell;
}
