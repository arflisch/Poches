using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class HoldingEditPage : ContentPage
{
    public HoldingEditPage(HoldingEditViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
