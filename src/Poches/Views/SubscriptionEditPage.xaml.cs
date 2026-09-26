using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class SubscriptionEditPage : ContentPage
{
    public SubscriptionEditPage(SubscriptionEditViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
