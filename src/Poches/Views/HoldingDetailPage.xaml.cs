using Poches.ViewModels;

namespace Poches.Views;

public partial class HoldingDetailPage : ContentPage
{
    private readonly HoldingDetailViewModel _viewModel;

    public HoldingDetailPage(HoldingDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RequestReload();
    }
}
