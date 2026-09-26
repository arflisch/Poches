using System.Text;
using System.Text.Json.Nodes;
using Poches.Core.Backup;
using Poches.Core.Models;

namespace Poches.Core.Tests;

public sealed class BackupEncryptionTests
{
    private const string Password = "Vacances-2026!";

    private static readonly BackupDocument Document = new()
    {
        ExportedAt = new DateTime(2026, 9, 26, 10, 0, 0),
        Currency = "€",
        Pockets = [new BackupPocket(1, "Épargne secrète", "🛟", "#10B981", 600000, new DateTime(2026, 1, 1))],
        Movements = [new BackupMovement(1, 123456, MovementKind.Deposit, "Prime de fin d'année", new DateTime(2026, 9, 1), null, null)],
        Subscriptions = [new BackupSubscription("Netflix", "🎬", "#F43F5E", 1349, BillingPeriod.Monthly, new DateTime(2026, 9, 27), true, new DateTime(2026, 1, 1))],
    };

    [Fact]
    public void Encrypted_backup_round_trips_with_the_right_password()
    {
        var file = BackupSerializer.WriteEncrypted(Document, Password);

        Assert.True(BackupSerializer.IsEncrypted(file));
        var restored = BackupSerializer.Read(file, Password);
        Assert.Equal("Épargne secrète", restored.Pockets.Single().Name);
        Assert.Equal(123456, restored.Movements.Single().AmountCents);
        Assert.Equal("Netflix", restored.Subscriptions.Single().Name);
    }

    [Fact]
    public void Nothing_is_readable_in_the_encrypted_file()
    {
        var text = Encoding.UTF8.GetString(BackupSerializer.WriteEncrypted(Document, Password));

        Assert.DoesNotContain("secrète", text);
        Assert.DoesNotContain("Netflix", text);
        Assert.DoesNotContain("123456", text);
        Assert.Contains("\"format\": \"poches-backup-encrypted\"", text);
        Assert.Contains("AES-256-GCM", text);
    }

    [Theory]
    [InlineData("vacances-2026!")]
    [InlineData("")]
    [InlineData("Vacances-2026! ")]
    public void A_wrong_password_is_refused(string password)
    {
        var file = BackupSerializer.WriteEncrypted(Document, Password);

        var error = Assert.Throws<BudgetException>(() => BackupSerializer.Read(file, password));
        Assert.Equal(BudgetError.WrongPassword, error.Error);
    }

    [Fact]
    public void An_encrypted_file_needs_a_password()
    {
        var file = BackupSerializer.WriteEncrypted(Document, Password);

        Assert.Equal(BudgetError.WrongPassword, Assert.Throws<BudgetException>(() => BackupSerializer.Read(file)).Error);
    }

    [Fact]
    public void A_modified_file_is_detected()
    {
        var json = JsonNode.Parse(BackupSerializer.WriteEncrypted(Document, Password))!;
        var ciphertext = Convert.FromBase64String(json["ciphertext"]!.GetValue<string>());
        ciphertext[10] ^= 0x01;
        json["ciphertext"] = Convert.ToBase64String(ciphertext);

        var error = Assert.Throws<BudgetException>(() => BackupSerializer.Read(Encoding.UTF8.GetBytes(json.ToJsonString()), Password));
        Assert.Equal(BudgetError.WrongPassword, error.Error);
    }

    [Fact]
    public void Two_backups_of_the_same_data_differ()
    {
        var first = BackupSerializer.WriteEncrypted(Document, Password);
        var second = BackupSerializer.WriteEncrypted(Document, Password);

        Assert.NotEqual(Convert.ToBase64String(first), Convert.ToBase64String(second));
    }

    [Fact]
    public void Accented_passwords_match_whatever_their_unicode_form()
    {
        var composed = "Épargne";                       // É as one character
        var decomposed = "Épargne";               // E + combining accent

        var file = BackupSerializer.WriteEncrypted(Document, composed);

        Assert.Equal("Netflix", BackupSerializer.Read(file, decomposed).Subscriptions.Single().Name);
    }

    [Fact]
    public async Task Plain_backups_from_earlier_versions_are_still_readable()
    {
        using var stream = new MemoryStream();
        await BackupSerializer.WriteAsync(Document, stream);
        var file = stream.ToArray();

        Assert.False(BackupSerializer.IsEncrypted(file));
        Assert.Equal("Netflix", BackupSerializer.Read(file).Subscriptions.Single().Name);
        Assert.Equal("Netflix", BackupSerializer.Read(file, "ignored").Subscriptions.Single().Name);
    }

    [Fact]
    public void Crafted_encryption_parameters_are_rejected()
    {
        var json = JsonNode.Parse(BackupSerializer.WriteEncrypted(Document, Password))!;
        json["iterations"] = 2_000_000_000;

        var error = Assert.Throws<BudgetException>(() => BackupSerializer.Read(Encoding.UTF8.GetBytes(json.ToJsonString()), Password));
        Assert.Equal(BudgetError.InvalidBackupFile, error.Error);
    }
}
