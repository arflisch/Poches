using CommunityToolkit.Mvvm.Messaging;
using Poches.Core.Data;
using Poches.Services;
using Poches.ViewModels;

namespace Poches;

public partial class App : Application
{
    private readonly AppShell _shell;

    public App(AppShell shell, BudgetStore store, AppSettings settings)
    {
        InitializeComponent();
        _shell = shell;

        // Every screen listens (weakly) to this single message instead of holding on to the store.
        store.Changed += (_, _) => WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        settings.Changed += (_, _) => WeakReferenceMessenger.Default.Send(new DataChangedMessage());
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_shell) { Title = "Poches" };
}
