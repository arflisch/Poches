using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class EditPocketPage : ContentPage
{
    public EditPocketPage(EditPocketViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
