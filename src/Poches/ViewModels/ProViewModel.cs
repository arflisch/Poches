using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

public sealed partial class ProViewModel : ObservableObject, ISheetViewModel
{
    private readonly ProService _pro;
    private readonly IDialogService _dialogs;

    public ProViewModel(ProService pro, IDialogService dialogs)
    {
        _pro = pro;
        _dialogs = dialogs;
        _isUnlocked = pro.IsUnlocked;
        _buyText = Loc.Get("Pro_Unlock");
        if (!pro.IsUnlocked)
            _ = LoadPriceAsync();
    }

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    public bool IsTestBuild => ProService.IsTestBuild;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    private bool _isUnlocked;

    public bool IsLocked => !IsUnlocked;

    /// <summary>"Unlock for €14.99" once the store gave the price.</summary>
    [ObservableProperty]
    private string _buyText;

    [ObservableProperty]
    private bool _isBusy;

    private async Task LoadPriceAsync()
    {
        if (await _pro.GetPriceAsync() is { } price)
            BuyText = Loc.Format("Pro_UnlockFor", price);
    }

    [RelayCommand]
    private async Task BuyAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            switch (await _pro.PurchaseAsync())
            {
                case ProPurchaseOutcome.Unlocked:
                    await UnlockedAsync(Loc.Get("Pro_Thanks"));
                    break;
                case ProPurchaseOutcome.Unavailable:
                    await _dialogs.AlertAsync(Loc.Get("Pro_UnavailableTitle"), Loc.Get("Pro_UnavailableText"));
                    break;
                case ProPurchaseOutcome.Failed:
                    await _dialogs.AlertAsync(Loc.Get("Pro_FailedTitle"), Loc.Get("Pro_FailedText"));
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            if (await _pro.RestoreAsync())
                await UnlockedAsync(Loc.Get("Pro_Restored"));
            else
                await _dialogs.AlertAsync(Loc.Get("Pro_NothingToRestoreTitle"), Loc.Get("Pro_NothingToRestoreText"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UnlockForTestingAsync()
    {
#if POCHES_PRO_TESTING
        _pro.SetUnlockedForTesting(true);
        await UnlockedAsync(Loc.Get("Pro_Thanks"));
#else
        await Task.CompletedTask;
#endif
    }

    [RelayCommand]
    private void LockForTesting()
    {
#if POCHES_PRO_TESTING
        _pro.SetUnlockedForTesting(false);
        IsUnlocked = false;
#endif
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");

    private async Task UnlockedAsync(string message)
    {
        IsUnlocked = true;
        SuccessToast.Show(message);
        await Shell.Current.GoToAsync("..");
    }
}
