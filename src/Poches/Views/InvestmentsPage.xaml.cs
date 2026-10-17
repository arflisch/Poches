using Poches.ViewModels;

namespace Poches.Views;

public partial class InvestmentsPage : ContentPage
{
    private readonly InvestmentsViewModel _viewModel;

    public InvestmentsPage(InvestmentsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Quotes move during the day (a fresh one is fetched at most every ten minutes).
        _viewModel.RequestReload();
    }
}
