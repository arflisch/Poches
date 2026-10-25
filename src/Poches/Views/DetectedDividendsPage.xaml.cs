using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class DetectedDividendsPage : ContentPage
{
    private readonly DetectedDividendsViewModel _viewModel;

    public DetectedDividendsPage(DetectedDividendsViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Errors are shown by the view model itself.
        _ = _viewModel.LoadAsync();
    }
}
