using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class ScheduledDepositPage : ContentPage
{
    public ScheduledDepositPage(ScheduledDepositViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
