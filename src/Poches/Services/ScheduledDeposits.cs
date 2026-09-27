using Poches.Core.Data;
using Poches.Localization;

namespace Poches.Services;

/// <summary>Adds the scheduled deposits that fell due, each time the app is opened or comes back (Poches Pro).</summary>
public sealed class ScheduledDeposits(BudgetStore store, ProService pro)
{
    public void ApplyDueSoon() => MainThread.BeginInvokeOnMainThread(async () =>
    {
        if (!pro.IsUnlocked)
            return;
        try
        {
            await store.ApplyDueScheduledDepositsAsync(DateTime.Today, Loc.Get("Scheduled_DefaultNote"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Scheduled deposits failed: {ex}");
        }
    });
}
