using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class BackupPasswordPage : ContentPage
{
    public BackupPasswordPage(BackupPasswordViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Let the sheet finish sliding in before bringing up the keyboard.
        await Task.Delay(400);
        PasswordEntry.Focus();
    }
}
