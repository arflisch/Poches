using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = _viewModel = viewModel;
    }

    private readonly SettingsViewModel _viewModel;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshPro();
        _ = _viewModel.RefreshReminderStatusAsync();
        // Back from the phone's settings, where notifications may just have been turned on.
        if (Window is { } window)
            window.Resumed += OnWindowResumed;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Window is { } window)
            window.Resumed -= OnWindowResumed;
    }

    private void OnWindowResumed(object? sender, EventArgs e) => _ = _viewModel.RefreshReminderStatusAsync();
}
