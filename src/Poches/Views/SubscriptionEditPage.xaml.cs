using Poches.ViewModels;

namespace Poches.Views;

public partial class SubscriptionEditPage : ContentPage
{
    public SubscriptionEditPage(SubscriptionEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
