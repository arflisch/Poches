using Poches.ViewModels;

namespace Poches.Views;

public partial class SubscriptionsPage : ContentPage
{
    private readonly SubscriptionsViewModel _viewModel;

    public SubscriptionsPage(SubscriptionsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Due dates depend on the current day, so refresh whenever the tab is shown.
        _viewModel.RequestReload();
    }
}
