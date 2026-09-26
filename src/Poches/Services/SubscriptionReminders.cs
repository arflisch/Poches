using CommunityToolkit.Mvvm.Messaging;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;
using Poches.Core.Data;
using Poches.Core.Subscriptions;
using Poches.Localization;
using Poches.ViewModels;

namespace Poches.Services;

/// <summary>
/// Keeps local notifications in sync with the subscriptions: one reminder at 9 am, a few days before each payment.
/// </summary>
public sealed class SubscriptionReminders
{
    private const int ReminderHour = 9;

    /// <summary>iOS keeps at most 64 pending notifications per app.</summary>
    private const int MaxScheduled = 60;

    private const int PerSubscription = 3;

    private static readonly TimeSpan Horizon = TimeSpan.FromDays(180);

    private readonly BudgetStore _store;
    private readonly AppSettings _settings;
    private readonly INotificationService _notifications;
    private CancellationTokenSource? _pendingRefresh;

    public SubscriptionReminders(BudgetStore store, AppSettings settings, INotificationService notifications)
    {
        _store = store;
        _settings = settings;
        _notifications = notifications;
        WeakReferenceMessenger.Default.Register<SubscriptionReminders, DataChangedMessage>(
            this, static (reminders, _) => reminders.RefreshSoon());
    }

    /// <summary>Reschedules shortly, coalescing bursts of changes into one pass.</summary>
    public void RefreshSoon()
    {
        _pendingRefresh?.Cancel();
        var cancellation = _pendingRefresh = new CancellationTokenSource();
        _ = RefreshAfterDelayAsync(cancellation.Token);
    }

    /// <summary>Asks for the notification permission the first time reminders are actually needed.</summary>
    public async Task EnsurePermissionAsync()
    {
        if (_settings.ReminderDaysBefore is null || !_notifications.IsSupported)
            return;
        // iOS refuses reminders scheduled before the permission is granted, so schedule them again right after.
        if (!await _notifications.AreNotificationsEnabled() && await _notifications.RequestNotificationPermission())
            RefreshSoon();
    }

    private async Task RefreshAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken);
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reminder scheduling failed: {ex}");
        }
    }

    private async Task RefreshAsync()
    {
        if (!_notifications.IsSupported)
            return;

        // Only pending reminders are replaced: the ones already delivered stay in the notification centre.
        var pending = await _notifications.GetPendingNotificationList();
        if (pending.Count > 0)
            _notifications.Cancel(pending.Select(p => p.NotificationId).ToArray());

        if (_settings.ReminderDaysBefore is not { } daysBefore)
            return;

        var now = DateTime.Now;
        var overview = await _store.GetSubscriptionOverviewAsync(now.Date);
        var formatter = _settings.Formatter;
        var reminders = overview.Active
            .SelectMany(s => BillingSchedule.Payments(s.Subscription.BillingAnchor, s.Subscription.Period, now.Date)
                .TakeWhile(payment => payment <= now.Date + Horizon)
                .Take(PerSubscription)
                .Select(payment => (s.Subscription, Payment: payment, NotifyAt: payment.AddDays(-daysBefore).AddHours(ReminderHour))))
            .Where(r => r.NotifyAt > now)
            .OrderBy(r => r.NotifyAt)
            .Take(MaxScheduled);

        foreach (var (subscription, payment, notifyAt) in reminders)
        {
            await _notifications.Show(new NotificationRequest
            {
                NotificationId = NotificationId(subscription.Id, payment),
                Title = $"{subscription.Icon} {subscription.Name}",
                Description = Loc.Format("Reminder_Body", formatter.Format(subscription.Amount), Loc.When(daysBefore)),
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = notifyAt,
                    // A reminder a few minutes late is fine and needs no "exact alarm" permission on Android.
                    Android = new AndroidScheduleOptions { ScheduleMode = AndroidScheduleMode.InexactAllowWhileIdle },
                },
            });
        }
    }

    /// <summary>One id per payment, so a delivered reminder is never mistaken for a pending one.</summary>
    private static int NotificationId(int subscriptionId, DateTime payment) =>
        subscriptionId * 10_000 + DateOnly.FromDateTime(payment).DayNumber % 10_000;
}
