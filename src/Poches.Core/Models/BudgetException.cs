namespace Poches.Core.Models;

/// <summary>A business rule was violated; the message is user-facing (French).</summary>
public sealed class BudgetException(string message) : Exception(message);
