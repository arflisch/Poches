using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class OperationEditPage : ContentPage
{
    public OperationEditPage(OperationEditViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
