using System.Text;
using Poches.Core.Backup;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;

namespace Poches.Core.Tests;

public sealed class BackupTests : IAsyncLifetime
{
    private readonly List<string> _paths = [];
    private BudgetStore _source = null!;
    private BudgetStore _target = null!;

    public Task InitializeAsync()
    {
        _source = CreateStore();
        _target = CreateStore();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _source.DisposeAsync();
        await _target.DisposeAsync();
        foreach (var path in _paths)
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
            File.Delete(file);
    }

    [Fact]
    public async Task Export_then_import_into_another_phone_restores_everything()
    {
        var now = new DateTime(2026, 9, 25, 10, 0, 0);
        await _source.SeedSampleDataAsync(now);
        var savings = (await _source.GetPocketSummariesAsync()).First(p => p.Pocket.Name == "Épargne de précaution");
        var holidays = (await _source.GetPocketSummariesAsync()).First(p => p.Pocket.Name == "Vacances");
        await _source.TransferAsync(savings.Pocket.Id, holidays.Pocket.Id, 100m, "Été", now);

        // The target already holds unrelated data, which must be replaced.
        await _target.SavePocketAsync(new Pocket { Name = "Ancienne poche" }, 999m);

        var restored = await RoundTripAsync(await _source.ExportAsync(now) with { Currency = "CHF" });
        await _target.ImportAsync(restored);

        var before = await _source.GetOverviewAsync(now);
        var after = await _target.GetOverviewAsync(now);
        Assert.Equal("CHF", restored.Currency);
        Assert.Equal(before.TotalCents, after.TotalCents);
        Assert.Equal(before.MonthDeltaCents, after.MonthDeltaCents);
        Assert.Equal(
            before.Pockets.Select(p => (p.Pocket.Name, p.Pocket.Icon, p.Pocket.ColorHex, p.Pocket.GoalCents, p.BalanceCents)),
            after.Pockets.Select(p => (p.Pocket.Name, p.Pocket.Icon, p.Pocket.ColorHex, p.Pocket.GoalCents, p.BalanceCents)));
        Assert.DoesNotContain(after.Pockets, p => p.Pocket.Name == "Ancienne poche");

        // Transfers still point to the right pocket and are still deleted as a pair.
        var restoredHolidays = after.Pockets.First(p => p.Pocket.Name == "Vacances").Pocket.Id;
        var restoredSavings = after.Pockets.First(p => p.Pocket.Name == "Épargne de précaution").Pocket.Id;
        var incoming = (await _target.GetMovementsAsync(restoredHolidays)).Single(m => m.Kind == MovementKind.TransferIn);
        Assert.Equal(restoredSavings, incoming.CounterpartPocketId);
        await _target.DeleteMovementAsync(incoming.Id);
        Assert.DoesNotContain(await _target.GetMovementsAsync(restoredSavings), m => m.Kind == MovementKind.TransferOut);
    }

    [Fact]
    public async Task Backup_file_is_readable_json()
    {
        await _source.SavePocketAsync(new Pocket { Name = "Épargne", Icon = "🛟", Goal = 2000m }, 150.5m);

        using var stream = new MemoryStream();
        await BackupSerializer.WriteAsync(await _source.ExportAsync(DateTime.Now), stream);
        var json = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("\"format\": \"poches-backup\"", json);
        Assert.Contains("\"kind\": \"Deposit\"", json);
        Assert.Contains("\"amountCents\": 15050", json);
        Assert.Contains("\"name\": \"Épargne\"", json);
        // Emojis are escaped by System.Text.Json but must come back intact.
        using var reread = new MemoryStream(stream.ToArray());
        Assert.Equal("🛟", (await BackupSerializer.ReadAsync(reread)).Pockets.Single().Icon);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"format\": \"something-else\", \"version\": 1}")]
    [InlineData("[]")]
    public async Task Reading_a_foreign_file_is_refused(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var error = await Assert.ThrowsAsync<BudgetException>(() => BackupSerializer.ReadAsync(stream));
        Assert.Equal(BudgetError.InvalidBackupFile, error.Error);
    }

    [Fact]
    public async Task Reading_a_backup_from_a_newer_version_is_refused()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"format\": \"poches-backup\", \"version\": 99}"));

        var error = await Assert.ThrowsAsync<BudgetException>(() => BackupSerializer.ReadAsync(stream));
        Assert.Equal(BudgetError.BackupFromNewerVersion, error.Error);
    }

    [Fact]
    public async Task Inconsistent_backup_is_refused_and_current_data_is_kept()
    {
        await _target.SavePocketAsync(new Pocket { Name = "À garder" }, 50m);
        var broken = new BackupDocument
        {
            Pockets = [new BackupPocket(1, "Vacances", "🏖️", "#F97316", null, DateTime.Now)],
            Movements = [new BackupMovement(42, 1000, MovementKind.Deposit, null, DateTime.Now, null, null)],
        };

        var error = await Assert.ThrowsAsync<BudgetException>(() => _target.ImportAsync(broken));
        Assert.Equal(BudgetError.CorruptBackup, error.Error);

        var remaining = Assert.Single(await _target.GetPocketSummariesAsync());
        Assert.Equal("À garder", remaining.Pocket.Name);
    }

    [Fact]
    public async Task Delete_all_empties_the_database()
    {
        await _source.SeedSampleDataAsync(DateTime.Now);
        var changes = 0;
        _source.Changed += (_, _) => changes++;

        await _source.DeleteAllAsync();

        var overview = await _source.GetOverviewAsync(DateTime.Now);
        Assert.Empty(overview.Pockets);
        Assert.Equal(0, overview.TotalCents);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(0, "aujourd'hui")]
    [InlineData(1, "hier")]
    [InlineData(12, "il y a 12 jours")]
    [InlineData(45, "le 11 août 2026")]
    public void Describes_backup_age(int daysAgo, string expected)
    {
        var now = new DateTime(2026, 9, 25, 8, 0, 0);
        var french = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        Assert.Equal(expected, RelativeDate.Describe(now.AddDays(-daysAgo), now, RelativeDateTexts.French, french));
    }

    [Fact]
    public void Describes_backup_age_in_another_language()
    {
        var now = new DateTime(2026, 9, 25, 8, 0, 0);
        var dutch = new RelativeDateTexts("vandaag", "gisteren", "{0} dagen geleden", "op {0}");
        var culture = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");

        Assert.Equal("3 dagen geleden", RelativeDate.Describe(now.AddDays(-3), now, dutch, culture));
        Assert.Equal("op 11 augustus 2026", RelativeDate.Describe(now.AddDays(-45), now, dutch, culture));
    }

    private BudgetStore CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"poches-backup-{Guid.NewGuid():N}.db3");
        _paths.Add(path);
        return new BudgetStore(path);
    }

    private static async Task<BackupDocument> RoundTripAsync(BackupDocument document)
    {
        using var stream = new MemoryStream();
        await BackupSerializer.WriteAsync(document, stream);
        stream.Position = 0;
        return await BackupSerializer.ReadAsync(stream);
    }
}
