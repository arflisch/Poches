using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class MovementPage : ContentPage
{
    public MovementPage(MovementViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
