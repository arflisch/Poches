using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Core.Subscriptions;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>Creates or edits a scheduled deposit into a pocket (Poches Pro).</summary>
public sealed partial class ScheduledDepositViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private readonly BudgetStore _store;
    private readonly IDialogService _dialogs;
    private readonly ScheduledDeposits _scheduler;
    private readonly MoneyFormatter _formatter;
    private ScheduledDeposit _schedule = new();
    private BillingPeriod _loadedPeriod;
    private DateTime _loadedNext;

    public ScheduledDepositViewModel(BudgetStore store, AppSettings settings, IDialogService dialogs, ScheduledDeposits scheduler)
    {
        _store = store;
        _dialogs = dialogs;
        _scheduler = scheduler;
        _formatter = new MoneyFormatter(settings.Currency, culture: Localizer.Instance.Culture);
        Currency = settings.Currency;
        AmountPlaceholder = 200m.ToString("0.00", Localizer.Instance.Culture);
        Periods =
        [
            new SelectableOption(nameof(BillingPeriod.Weekly), 0, 5, SelectPeriod) { Label = Loc.Get("Period_Week") },
            new SelectableOption(nameof(BillingPeriod.Monthly), 1, 5, SelectPeriod) { Label = Loc.Get("Period_Month") },
            new SelectableOption(nameof(BillingPeriod.Quarterly), 2, 5, SelectPeriod) { Label = Loc.Get("Period_Quarter") },
            new SelectableOption(nameof(BillingPeriod.Semiannual), 3, 5, SelectPeriod) { Label = Loc.Get("Period_Semester") },
            new SelectableOption(nameof(BillingPeriod.Yearly), 4, 5, SelectPeriod) { Label = Loc.Get("Period_Year") },
        ];
        OnPeriodChanged(Period);
    }

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public IReadOnlyList<SelectableOption> Periods { get; }

    public string Currency { get; }

    public string AmountPlaceholder { get; }

    /// <summary>No deposit can be scheduled in the past (it would be added right away, several times).</summary>
    public DateTime MinimumDate { get; } = DateTime.Today;

    [ObservableProperty]
    private string _title = Loc.Get("Scheduled_NewTitle");

    [ObservableProperty]
    private string _pocketName = string.Empty;

    [ObservableProperty]
    private bool _isNew = true;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private BillingPeriod _period = BillingPeriod.Monthly;

    [ObservableProperty]
    private DateTime? _nextDate = DateTime.Today;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private string _hint = string.Empty;

    [ObservableProperty]
    private bool _hasHint;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && int.TryParse(id?.ToString(), out var scheduleId))
            _ = LoadAsync(scheduleId);
        else if (query.TryGetValue("pocketId", out var pocket) && int.TryParse(pocket?.ToString(), out var pocketId))
            _ = LoadPocketAsync(pocketId);
    }

    private async Task LoadPocketAsync(int pocketId)
    {
        _schedule.PocketId = pocketId;
        PocketName = (await _store.GetPocketSummaryAsync(pocketId))?.Pocket.Name ?? string.Empty;
    }

    private async Task LoadAsync(int scheduleId)
    {
        var schedule = (await _store.GetScheduledDepositsAsync()).FirstOrDefault(s => s.Id == scheduleId);
        if (schedule is null)
            return;

        _schedule = schedule;
        _loadedPeriod = schedule.Period;
        _loadedNext = BudgetStore.NextDeposit(schedule);
        IsNew = false;
        Title = Loc.Get("Scheduled_EditTitle");
        await LoadPocketAsync(schedule.PocketId);
        AmountText = schedule.Amount.ToString("0.00", Localizer.Instance.Culture);
        Period = schedule.Period;
        NextDate = _loadedNext < MinimumDate ? MinimumDate : _loadedNext;
        Note = schedule.Note ?? string.Empty;
        IsActive = schedule.IsActive;
    }

    private void SelectPeriod(SelectableOption option) => Period = Enum.Parse<BillingPeriod>(option.Value);

    partial void OnPeriodChanged(BillingPeriod value)
    {
        foreach (var option in Periods)
            option.IsSelected = option.Value == value.ToString();
        UpdateHint();
    }

    partial void OnAmountTextChanged(string value)
    {
        HasError = false;
        UpdateHint();
    }

    /// <summary>"That's €2,400 a year".</summary>
    private void UpdateHint()
    {
        HasHint = AmountParser.TryParse(AmountText, out var amount) && amount > 0;
        if (HasHint)
            Hint = Loc.Format("SubEdit_CostPerYear", _formatter.Format(BillingSchedule.YearlyCost(amount, Period)));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!AmountParser.TryParse(AmountText, out var amount) || amount <= 0)
        {
            ShowError(Loc.Get("Movement_EnterAmount"));
            return;
        }

        var next = (NextDate ?? DateTime.Today).Date;
        _schedule.Amount = amount;
        _schedule.Note = Note;
        _schedule.IsActive = IsActive;
        // Keep the original anchor when the schedule is untouched, so a deposit on the 31st stays on the 31st.
        if (IsNew || Period != _loadedPeriod || next != _loadedNext)
            _schedule.Anchor = next;
        _schedule.Period = Period;

        try
        {
            await _store.SaveScheduledDepositAsync(_schedule, DateTime.Today);
            _scheduler.ApplyDueSoon();
            SuccessToast.Show(Loc.Get("Toast_Saved"));
            await Shell.Current.GoToAsync("..");
        }
        catch (BudgetException ex)
        {
            ShowError(Loc.Error(ex));
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            Loc.Get("Scheduled_DeleteTitle"), Loc.Get("Scheduled_DeleteText"), Loc.Get("Edit_DeleteConfirm"));
        if (!confirmed)
            return;

        await _store.DeleteScheduledDepositAsync(_schedule.Id);
        Palette.Haptic();
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
