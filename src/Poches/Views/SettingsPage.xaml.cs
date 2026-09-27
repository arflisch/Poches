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
    }
}
