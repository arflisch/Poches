using CommunityToolkit.Mvvm.Messaging;
using Poches.Core.Data;
using Poches.Services;
using Poches.ViewModels;

namespace Poches;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly SubscriptionReminders _reminders;
    private readonly IPreferences _preferences;
    private readonly AppLock _lock;
    private readonly ScheduledDeposits _scheduledDeposits;

    public App(
        AppShell shell,
        BudgetStore store,
        AppSettings settings,
        SubscriptionReminders reminders,
        IPreferences preferences,
        AppLock appLock,
        ProService pro,
        ScheduledDeposits scheduledDeposits)
    {
        InitializeComponent();
        _shell = shell;
        _reminders = reminders;
        _preferences = preferences;
        _lock = appLock;
        _scheduledDeposits = scheduledDeposits;
        pro.Changed += (_, _) =>
        {
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            scheduledDeposits.ApplyDueSoon();
        };

        // Every screen listens (weakly) to this single message instead of holding on to the store.
        store.Changed += (_, _) => WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        settings.Changed += (_, _) => WeakReferenceMessenger.Default.Send(new DataChangedMessage());
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell) { Title = "Poches" };
        DesktopWindow.Configure(window, _preferences);
        _lock.Attach(window);
        // Reminders cover a rolling window of upcoming payments: top it up whenever the app is opened.
        window.Created += (_, _) => _reminders.RefreshSoon();
        window.Resumed += (_, _) => _reminders.RefreshSoon();
        // Scheduled deposits (Poches Pro) that fell due while the app was closed are added when it opens.
        window.Created += (_, _) => _scheduledDeposits.ApplyDueSoon();
        window.Resumed += (_, _) => _scheduledDeposits.ApplyDueSoon();
        return window;
    }
}
