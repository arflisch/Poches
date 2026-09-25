using Poches.Core.Backup;
using Poches.Core.Data;

namespace Poches.Services;

/// <summary>Exports the data to a JSON file the user can keep anywhere (iCloud Drive, mail…) and restores it.</summary>
public sealed class BackupService(BudgetStore store, AppSettings settings, IShare share, BackupFilePicker filePicker)
{
    /// <summary>A stale reminder is shown on the home page past this age.</summary>
    public static readonly TimeSpan ReminderAge = TimeSpan.FromDays(30);

    /// <summary>Writes a backup file and opens the share sheet so the user decides where to keep it.</summary>
    public async Task ShareBackupAsync()
    {
        var now = DateTime.Now;
        var document = await store.ExportAsync(now) with { Currency = settings.Currency };

        var path = Path.Combine(FileSystem.CacheDirectory, $"poches-{now:yyyy-MM-dd}.json");
        await using (var file = File.Create(path))
            await BackupSerializer.WriteAsync(document, file);

        await share.RequestAsync(new ShareFileRequest
        {
            Title = Localization.Loc.Get("Backup_ShareTitle"),
            File = new ShareFile(path, "application/json"),
        });
        settings.LastBackupAt = now;
    }

    /// <summary>Lets the user pick a backup file; returns null if they cancel.</summary>
    /// <exception cref="Core.Models.BudgetException">The file is not a readable Poches backup.</exception>
    public async Task<BackupDocument?> PickBackupAsync()
    {
        await using var stream = await filePicker.OpenAsync();
        return stream is null ? null : await BackupSerializer.ReadAsync(stream);
    }

    public async Task RestoreAsync(BackupDocument backup)
    {
        await store.ImportAsync(backup);
        if (backup.Currency is { } currency && AppSettings.Currencies.Contains(currency))
            settings.Currency = currency;
    }
}
