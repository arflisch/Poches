using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Services;

namespace Poches.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly BudgetStore _store;
    private readonly AppSettings _settings;
    private readonly BackupService _backup;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(BudgetStore store, AppSettings settings, BackupService backup, IDialogService dialogs)
    {
        _store = store;
        _settings = settings;
        _backup = backup;
        _dialogs = dialogs;
        _currency = settings.Currency;
        _hideAmounts = settings.HideAmounts;
        RefreshBackupText();
    }

    public string AppVersion => $"Poches {AppInfo.Current.VersionString}";

    [ObservableProperty]
    private string _currency;

    [ObservableProperty]
    private bool _hideAmounts;

    [ObservableProperty]
    private string _lastBackupText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnHideAmountsChanged(bool value) => _settings.HideAmounts = value;

    [RelayCommand]
    private async Task ChooseCurrencyAsync()
    {
        var options = AppSettings.Currencies.Select(c => c == Currency ? $"{c}  ✓" : c).ToArray();
        var choice = await _dialogs.ChooseAsync("Devise d'affichage", null, options);
        if (choice is null)
            return;
        Currency = _settings.Currency = choice.Replace("✓", string.Empty).Trim();
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        IsBusy = true;
        try
        {
            await _backup.ShareBackupAsync();
            RefreshBackupText();
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Sauvegarde impossible", $"Le fichier n'a pas pu être créé. ({ex.Message})");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        try
        {
            var backup = await _backup.PickBackupAsync();
            if (backup is null)
                return;

            var confirmed = await _dialogs.ConfirmAsync(
                "Restaurer cette sauvegarde ?",
                $"Sauvegarde du {backup.ExportedAt:d MMMM yyyy} : {Plural(backup.Pockets.Count, "poche")}, " +
                $"{Plural(backup.Movements.Count, "mouvement")}.\n\nTes données actuelles seront remplacées.",
                "Restaurer");
            if (!confirmed)
                return;

            IsBusy = true;
            await _backup.RestoreAsync(backup);
            Currency = _settings.Currency;
            IsBusy = false;
            Palette.Haptic();
            await _dialogs.AlertAsync("C'est fait", "Tes poches ont été restaurées.");
        }
        catch (BudgetException ex)
        {
            await _dialogs.AlertAsync("Restauration impossible", ex.Message);
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("Restauration impossible", $"Le fichier n'a pas pu être lu. ({ex.Message})");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAllAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Tout effacer ?",
            "Toutes tes poches et leur historique seront définitivement supprimés de ce téléphone. " +
            "Fais une sauvegarde avant si tu veux pouvoir les récupérer.",
            "Tout effacer");
        if (!confirmed)
            return;

        await _store.DeleteAllAsync();
        Palette.Haptic();
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");

    private void RefreshBackupText() =>
        LastBackupText = _settings.LastBackupAt is { } date
            ? $"Dernière sauvegarde : {RelativeDate.Describe(date, DateTime.Now)}"
            : "Aucune sauvegarde pour l'instant";

    private static string Plural(int count, string word) => $"{count} {word}{(count > 1 ? "s" : string.Empty)}";
}
