using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class ProPage : ContentPage
{
    public ProPage(ProViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
