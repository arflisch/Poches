using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Poches.Core.Models;

namespace Poches.Core.Backup;

/// <summary>Reads and writes <see cref="BackupDocument"/> as JSON (source-generated, safe for trimmed iOS builds).</summary>
public static class BackupSerializer
{
    // Keep accents and emojis readable in the file: it is never embedded in HTML.
    private static readonly BackupJsonContext Context = new(
        new JsonSerializerOptions(BackupJsonContext.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    public static Task WriteAsync(BackupDocument document, Stream stream, CancellationToken cancellationToken = default) =>
        JsonSerializer.SerializeAsync(stream, document, Context.BackupDocument, cancellationToken);

    /// <exception cref="BudgetException">The stream is not a Poches backup this version can read.</exception>
    public static async Task<BackupDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        BackupDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync(stream, Context.BackupDocument, cancellationToken);
        }
        catch (JsonException)
        {
            throw new BudgetException("Ce fichier n'est pas une sauvegarde Poches valide.");
        }

        if (document is null || document.Format != BackupDocument.FormatName)
            throw new BudgetException("Ce fichier n'est pas une sauvegarde Poches valide.");
        if (document.Version > BackupDocument.CurrentVersion)
            throw new BudgetException("Cette sauvegarde vient d'une version plus récente de Poches. Mets l'app à jour pour la restaurer.");
        return document;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(BackupDocument))]
internal sealed partial class BackupJsonContext : JsonSerializerContext;
