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

    public App(AppShell shell, BudgetStore store, AppSettings settings, SubscriptionReminders reminders, IPreferences preferences)
    {
        InitializeComponent();
        _shell = shell;
        _reminders = reminders;
        _preferences = preferences;

        // Every screen listens (weakly) to this single message instead of holding on to the store.
        store.Changed += (_, _) => WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        settings.Changed += (_, _) => WeakReferenceMessenger.Default.Send(new DataChangedMessage());
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell) { Title = "Poches" };
        DesktopWindow.Configure(window, _preferences);
        // Reminders cover a rolling window of upcoming payments: top it up whenever the app is opened.
        window.Created += (_, _) => _reminders.RefreshSoon();
        window.Resumed += (_, _) => _reminders.RefreshSoon();
        return window;
    }
}
