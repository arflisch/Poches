using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class PurchaseEditPage : ContentPage
{
    public PurchaseEditPage(PurchaseEditViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
