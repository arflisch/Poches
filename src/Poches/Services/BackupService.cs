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
    /// Asks for a password, writes an encrypted backup file and opens the share sheet (the Save panel on a Mac)
    /// so the user decides where to keep it.
    /// </summary>
    public async Task<BackupOutcome> ShareBackupAsync()
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
            return BackupOutcome.Cancelled;

        var path = Path.Combine(FileSystem.CacheDirectory, $"poches-{now:yyyy-MM-dd}.json");
        await File.WriteAllBytesAsync(path, encrypted);

        await PresentationGuard.WaitUntilSettledAsync();
#if MACCATALYST
        // On a Mac, a Save panel is expected rather than a share menu.
        _ = share;
        var outcome = await filePicker.SaveAsync(path) ? BackupOutcome.Saved : BackupOutcome.Cancelled;
#elif IOS
        // Unlike MAUI's share, the native sheet says whether the file was saved or sent, or the sheet closed.
        _ = share;
        var outcome = await filePicker.ShareAsync(path) ? BackupOutcome.Saved : BackupOutcome.Cancelled;
#else
        await share.RequestAsync(new ShareFileRequest
        {
            Title = Localization.Loc.Get("Backup_ShareTitle"),
            File = new ShareFile(path, "application/json"),
        });
        // Android does not tell whether the file actually went anywhere.
        var outcome = BackupOutcome.HandedOver;
#endif
        if (outcome != BackupOutcome.Cancelled)
            settings.LastBackupAt = now;
        return outcome;
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

public enum BackupOutcome
{
    /// <summary>The user closed the password sheet, the share sheet or the Save panel.</summary>
    Cancelled,

    /// <summary>The file was saved or sent.</summary>
    Saved,

    /// <summary>The file was handed to the system share menu, which does not report what happened next.</summary>
    HandedOver,
}
