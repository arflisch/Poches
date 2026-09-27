using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class SplitPage : ContentPage
{
    public SplitPage(SplitViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
