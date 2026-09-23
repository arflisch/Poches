using Poches.ViewModels;

namespace Poches.Views;

public partial class EditPocketPage : ContentPage
{
    public EditPocketPage(EditPocketViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
