using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class InvestmentPlanPage : ContentPage
{
    private readonly InvestmentPlanViewModel _viewModel;

    public InvestmentPlanPage(InvestmentPlanViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
