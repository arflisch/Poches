using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, ISheetViewModel
{
    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    private readonly BudgetStore _store;
    private readonly AppSettings _settings;
    private readonly BackupService _backup;
    private readonly IDialogService _dialogs;
    private readonly SubscriptionReminders _reminders;

    public SettingsViewModel(
        BudgetStore store, AppSettings settings, BackupService backup, IDialogService dialogs, SubscriptionReminders reminders)
    {
        _store = store;
        _settings = settings;
        _backup = backup;
        _dialogs = dialogs;
        _reminders = reminders;
        _reminderText = ReminderLabel(settings.ReminderDaysBefore);
        _currency = settings.Currency;
        _languageName = settings.Language.NativeName;
        _hideAmounts = settings.HideAmounts;
        RefreshBackupText();
    }

    public string AppVersion => $"Poches {AppInfo.Current.VersionString}";

    [ObservableProperty]
    private string _currency;

    [ObservableProperty]
    private string _languageName;

    [ObservableProperty]
    private string _reminderText;

    [ObservableProperty]
    private bool _hideAmounts;

    [ObservableProperty]
    private string _lastBackupText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnHideAmountsChanged(bool value) => _settings.HideAmounts = value;

    [RelayCommand]
    private async Task ChooseLanguageAsync()
    {
        var current = _settings.Language;
        var options = Localizer.Languages.Select(l => l == current ? $"{l.NativeName}  ✓" : l.NativeName).ToArray();
        var choice = await _dialogs.ChooseAsync(Loc.Get("Settings_Language"), null, options);
        var language = Localizer.Languages.FirstOrDefault(l => choice?.StartsWith(l.NativeName, StringComparison.Ordinal) == true);
        if (language is null || language == current)
            return;

        _settings.Language = language;
        LanguageName = language.NativeName;
        ReminderText = ReminderLabel(_settings.ReminderDaysBefore);
        RefreshBackupText();
    }

    [RelayCommand]
    private async Task ChooseReminderAsync()
    {
        var current = _settings.ReminderDaysBefore;
        var options = AppSettings.ReminderChoices
            .Select(days => days == current ? $"{ReminderLabel(days)}  ✓" : ReminderLabel(days))
            .ToArray();
        var choice = await _dialogs.ChooseAsync(Loc.Get("Settings_Reminder"), null, options);
        if (choice is null)
            return;

        var days = AppSettings.ReminderChoices.First(d => choice.StartsWith(ReminderLabel(d), StringComparison.Ordinal));
        _settings.ReminderDaysBefore = days;
        ReminderText = ReminderLabel(days);
        await _reminders.EnsurePermissionAsync();
    }

    [RelayCommand]
    private async Task ChooseCurrencyAsync()
    {
        var options = AppSettings.Currencies.Select(c => c == Currency ? $"{c}  ✓" : c).ToArray();
        var choice = await _dialogs.ChooseAsync(Loc.Get("Settings_CurrencyTitle"), null, options);
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
            if (await _backup.ShareBackupAsync())
                RefreshBackupText();
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Settings_BackupFailed"), Loc.Format("Settings_BackupFailedText", ex.Message));
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
                Loc.Get("Settings_RestoreTitle"),
                Loc.Format(
                    "Settings_RestoreText",
                    backup.ExportedAt.ToString("d MMMM yyyy", Localizer.Instance.Culture),
                    Loc.Count(backup.Pockets.Count, "Pocket"),
                    Loc.Count(backup.Movements.Count, "Movement"),
                    Loc.Count(backup.Subscriptions.Count, "Subscription")),
                Loc.Get("Settings_RestoreConfirm"));
            if (!confirmed)
                return;

            IsBusy = true;
            await _backup.RestoreAsync(backup);
            Currency = _settings.Currency;
            IsBusy = false;
            Palette.Haptic();
            await _dialogs.AlertAsync(Loc.Get("Settings_RestoreDone"), Loc.Get("Settings_RestoreDoneText"));
        }
        catch (BudgetException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Settings_RestoreFailed"), Loc.Error(ex));
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Settings_RestoreFailed"), Loc.Format("Settings_RestoreFailedText", ex.Message));
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
            Loc.Get("Settings_DeleteAllTitle"),
            Loc.Get("Settings_DeleteAllText"),
            Loc.Get("Settings_DeleteAll"));
        if (!confirmed)
            return;

        await _store.DeleteAllAsync();
        Palette.Haptic();
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");

    private static string ReminderLabel(int? daysBefore) => daysBefore switch
    {
        null => Loc.Get("Reminder_Off"),
        0 => Loc.Get("Reminder_SameDay"),
        1 => Loc.Get("Reminder_DayBefore"),
        7 => Loc.Get("Reminder_Week"),
        _ => Loc.Format("Reminder_DaysBefore", daysBefore),
    };

    private void RefreshBackupText() =>
        LastBackupText = _settings.LastBackupAt is { } date
            ? Loc.Format("Settings_LastBackup", Loc.RelativeDate(date))
            : Loc.Get("Settings_NoBackup");

}
