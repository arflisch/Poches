using Poches.Core.Backup;
using Poches.Core.Data;
using Poches.Core.Models;
using Poches.ViewModels;

namespace Poches.Services;

/// <summary>Exports the data to a JSON file the user can keep anywhere (iCloud Drive, mail…) and restores it.</summary>
public sealed class BackupService(
    BudgetStore store, AppSettings settings, IShare share, BackupFilePicker filePicker, PasswordPrompt passwordPrompt)
{
    /// <summary>A stale reminder is shown on the home page past this age.</summary>
    public static readonly TimeSpan ReminderAge = TimeSpan.FromDays(30);

    /// <summary>
    /// Asks for a password, writes an encrypted backup file and opens the share sheet so the user decides where
    /// to keep it. Returns false if the user cancelled.
    /// </summary>
    public async Task<bool> ShareBackupAsync()
    {
        var now = DateTime.Now;
        var document = await store.ExportAsync(now) with { Currency = settings.Currency };

        byte[]? encrypted = null;
        var password = await passwordPrompt.AskAsync(PasswordPromptMode.Create, async chosen =>
        {
            // Key derivation is deliberately slow: keep it off the UI thread.
            encrypted = await Task.Run(() => BackupSerializer.WriteEncrypted(document, chosen));
            return null;
        });
        if (password is null || encrypted is null)
            return false;

        var path = Path.Combine(FileSystem.CacheDirectory, $"poches-{now:yyyy-MM-dd}.json");
        await File.WriteAllBytesAsync(path, encrypted);

        await share.RequestAsync(new ShareFileRequest
        {
            Title = Localization.Loc.Get("Backup_ShareTitle"),
            File = new ShareFile(path, "application/json"),
        });
        settings.LastBackupAt = now;
        return true;
    }

    /// <summary>Lets the user pick a backup file, asking for its password when it is encrypted; null if cancelled.</summary>
    /// <exception cref="Core.Models.BudgetException">The file is not a readable Poches backup.</exception>
    public async Task<BackupDocument?> PickBackupAsync()
    {
        byte[] file;
        await using (var stream = await filePicker.OpenAsync())
        {
            if (stream is null)
                return null;
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            file = memory.ToArray();
        }

        // Backups made before encryption was introduced open directly.
        if (!BackupSerializer.IsEncrypted(file))
            return BackupSerializer.Read(file);

        BackupDocument? document = null;
        var password = await passwordPrompt.AskAsync(PasswordPromptMode.Unlock, async candidate =>
        {
            try
            {
                document = await Task.Run(() => BackupSerializer.Read(file, candidate));
                return null;
            }
            catch (BudgetException ex) when (ex.Error == BudgetError.WrongPassword)
            {
                return Localization.Loc.Error(ex);
            }
        });
        return password is null ? null : document;
    }

    public async Task RestoreAsync(BackupDocument backup)
    {
        await store.ImportAsync(backup);
        if (backup.Currency is { } currency && AppSettings.Currencies.Contains(currency))
            settings.Currency = currency;
    }
}
