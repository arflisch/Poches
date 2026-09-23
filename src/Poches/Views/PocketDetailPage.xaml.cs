using Poches.ViewModels;

namespace Poches.Views;

public partial class PocketDetailPage : ContentPage
{
    public PocketDetailPage(PocketDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
