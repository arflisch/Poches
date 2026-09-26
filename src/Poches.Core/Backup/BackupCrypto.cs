using System.Security.Cryptography;
using System.Text;
using Poches.Core.Models;

namespace Poches.Core.Backup;

/// <summary>
/// Password-based encryption of backup files: AES-256-GCM (confidentiality and tamper detection) with a key
/// derived from the password by PBKDF2-HMAC-SHA256, so guessing passwords offline stays slow.
/// </summary>
internal static class BackupCrypto
{
    public const string FormatName = "poches-backup-encrypted";
    public const int CurrentVersion = 1;

    /// <summary>OWASP recommendation for PBKDF2-HMAC-SHA256.</summary>
    private const int Iterations = 600_000;

    /// <summary>Bounds accepted when reading, so a crafted file cannot make the app spin for minutes.</summary>
    private const int MinIterations = 10_000;
    private const int MaxIterations = 5_000_000;

    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    /// <summary>Binds the ciphertext to this format, so it cannot be passed off as something else.</summary>
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes($"{FormatName}/{CurrentVersion}");

    public static EncryptedBackup Encrypt(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        var key = DeriveKey(password, salt, Iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return new EncryptedBackup
        {
            Iterations = Iterations,
            Salt = salt,
            Nonce = nonce,
            Tag = tag,
            Ciphertext = ciphertext,
        };
    }

    /// <exception cref="BudgetException">Wrong password, tampered file or unsupported parameters.</exception>
    public static byte[] Decrypt(EncryptedBackup backup, string password)
    {
        if (backup.Version > CurrentVersion)
            throw new BudgetException(BudgetError.BackupFromNewerVersion, $"Encrypted backup version {backup.Version} is not supported.");
        if (backup.Salt.Length != SaltSize || backup.Nonce.Length != NonceSize || backup.Tag.Length != TagSize
            || backup.Iterations is < MinIterations or > MaxIterations)
            throw new BudgetException(BudgetError.InvalidBackupFile, "Invalid encryption parameters.");

        var plaintext = new byte[backup.Ciphertext.Length];
        var key = DeriveKey(password, backup.Salt, backup.Iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(backup.Nonce, backup.Ciphertext, backup.Tag, plaintext, AssociatedData);
            return plaintext;
        }
        catch (CryptographicException)
        {
            // GCM cannot tell a wrong password from a modified file: both fail authentication.
            throw new BudgetException(BudgetError.WrongPassword, "The backup could not be decrypted.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        // Normalise so an accented password typed on another device gives the same key.
        var passwordBytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC));
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}

/// <summary>On-disk envelope of an encrypted backup; the ciphertext is a regular <see cref="BackupDocument"/> JSON.</summary>
internal sealed record EncryptedBackup
{
    public string Format { get; init; } = BackupCrypto.FormatName;

    public int Version { get; init; } = BackupCrypto.CurrentVersion;

    public string Kdf { get; init; } = "PBKDF2-HMAC-SHA256";

    public string Cipher { get; init; } = "AES-256-GCM";

    public int Iterations { get; init; }

    public byte[] Salt { get; init; } = [];

    public byte[] Nonce { get; init; } = [];

    public byte[] Tag { get; init; } = [];

    public byte[] Ciphertext { get; init; } = [];
}
