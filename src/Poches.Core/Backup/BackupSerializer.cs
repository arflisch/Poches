using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Poches.Core.Models;

namespace Poches.Core.Backup;

/// <summary>
/// Reads and writes backup files (source-generated JSON, safe for trimmed iOS builds). Files written by the app
/// are encrypted with a password; plain files from earlier versions can still be read.
/// </summary>
public static class BackupSerializer
{
    // Keep accents and emojis readable in the file: it is never embedded in HTML.
    private static readonly BackupJsonContext Context = new(
        new JsonSerializerOptions(BackupJsonContext.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>Serialises and encrypts <paramref name="document"/>. Slow on purpose (key derivation): call it off the UI thread.</summary>
    public static byte[] WriteEncrypted(BackupDocument document, string password)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(document, Context.BackupDocument);
        return JsonSerializer.SerializeToUtf8Bytes(BackupCrypto.Encrypt(plaintext, password), Context.EncryptedBackup);
    }

    /// <summary>Whether the file needs a password to be read.</summary>
    public static bool IsEncrypted(byte[] file)
    {
        try
        {
            using var json = JsonDocument.Parse(file);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("format", out var format)
                && format.ValueKind == JsonValueKind.String
                && format.GetString() == BackupCrypto.FormatName;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads a backup file, decrypting it with <paramref name="password"/> when it is encrypted.</summary>
    /// <exception cref="BudgetException">Not a Poches backup, too recent, or wrong password.</exception>
    public static BackupDocument Read(byte[] file, string? password = null)
    {
        if (IsEncrypted(file))
        {
            if (password is null)
                throw new BudgetException(BudgetError.WrongPassword, "This backup is encrypted.");
            var envelope = Deserialize(file, Context.EncryptedBackup);
            file = BackupCrypto.Decrypt(envelope, password);
        }

        var document = Deserialize(file, Context.BackupDocument);
        if (document.Format != BackupDocument.FormatName)
            throw new BudgetException(BudgetError.InvalidBackupFile, "Not a Poches backup file.");
        if (document.Version > BackupDocument.CurrentVersion)
            throw new BudgetException(BudgetError.BackupFromNewerVersion, $"Backup version {document.Version} is newer than supported version {BackupDocument.CurrentVersion}.");

        // The source generator assigns every init property, so a list missing from the file (subscriptions in a
        // version 1 backup) arrives as null instead of keeping its empty default.
        return document with
        {
            Pockets = document.Pockets ?? [],
            Movements = document.Movements ?? [],
            Subscriptions = document.Subscriptions ?? [],
        };
    }

    /// <summary>Plain, unencrypted JSON, as written by versions before encryption. Kept for tests and tooling.</summary>
    public static Task WriteAsync(BackupDocument document, Stream stream, CancellationToken cancellationToken = default) =>
        JsonSerializer.SerializeAsync(stream, document, Context.BackupDocument, cancellationToken);

    /// <inheritdoc cref="Read"/>
    public static async Task<BackupDocument> ReadAsync(Stream stream, string? password = null, CancellationToken cancellationToken = default)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return Read(memory.ToArray(), password);
    }

    private static T Deserialize<T>(byte[] file, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(file, typeInfo)
                ?? throw new BudgetException(BudgetError.InvalidBackupFile, "Not a Poches backup file.");
        }
        catch (JsonException)
        {
            throw new BudgetException(BudgetError.InvalidBackupFile, "Not a Poches backup file.");
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(BackupDocument))]
[JsonSerializable(typeof(EncryptedBackup))]
internal sealed partial class BackupJsonContext : JsonSerializerContext;
