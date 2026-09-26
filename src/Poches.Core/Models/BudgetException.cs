namespace Poches.Core.Models;

/// <summary>Why a budget operation was refused. The app translates each code for the user.</summary>
public enum BudgetError
{
    EmptyName,
    EmptySubscriptionName,
    InvalidInitialAmount,
    InvalidAmount,
    AmountTooLarge,
    InsufficientFunds,
    SamePocket,
    PocketNotFound,
    MovementNotFound,
    BalanceWouldBeNegative,
    InvalidBackupFile,
    BackupFromNewerVersion,
    CorruptBackup,
    WrongPassword,
}

/// <summary>A business rule was violated. <see cref="Exception.Message"/> is for logs; show <see cref="Error"/> translated.</summary>
public sealed class BudgetException(BudgetError error, string message) : Exception(message)
{
    public BudgetError Error { get; } = error;
}
