using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class ContributionPage : ContentPage
{
    private readonly ContributionViewModel _viewModel;

    public ContributionPage(ContributionViewModel viewModel)
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
