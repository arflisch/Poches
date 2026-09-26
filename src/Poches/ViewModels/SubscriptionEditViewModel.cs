using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Core.Subscriptions;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

public sealed partial class SubscriptionEditViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    private readonly BudgetStore _store;
    private readonly IDialogService _dialogs;
    private readonly SubscriptionReminders _reminders;
    private readonly MoneyFormatter _formatter;
    private Subscription _subscription = new();
    private BillingPeriod _loadedPeriod;
    private DateTime _loadedNextPayment;

    public SubscriptionEditViewModel(BudgetStore store, AppSettings settings, IDialogService dialogs, SubscriptionReminders reminders)
    {
        _store = store;
        _dialogs = dialogs;
        _reminders = reminders;
        // Prices being typed are always shown, even in privacy mode.
        _formatter = new MoneyFormatter(settings.Currency, culture: Localizer.Instance.Culture);
        Currency = settings.Currency;
        PricePlaceholder = 9.99m.ToString("0.00", Localizer.Instance.Culture);
        Periods =
        [
            new SelectableOption(nameof(BillingPeriod.Weekly), 0, 4, SelectPeriod) { Label = Loc.Get("Period_Week") },
            new SelectableOption(nameof(BillingPeriod.Monthly), 1, 4, SelectPeriod) { Label = Loc.Get("Period_Month") },
            new SelectableOption(nameof(BillingPeriod.Quarterly), 2, 4, SelectPeriod) { Label = Loc.Get("Period_Quarter") },
            new SelectableOption(nameof(BillingPeriod.Yearly), 3, 4, SelectPeriod) { Label = Loc.Get("Period_Year") },
        ];
        OnPeriodChanged(Period);
    }

    public AppearanceSelection Appearance { get; } = new(Palette.SubscriptionEmojis);

    public IReadOnlyList<SelectableOption> Periods { get; }

    public string Currency { get; }

    public string PricePlaceholder { get; }

    [ObservableProperty]
    private string _title = Loc.Get("SubEdit_NewTitle");

    [ObservableProperty]
    private bool _isNew = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private BillingPeriod _period = BillingPeriod.Monthly;

    [ObservableProperty]
    private DateTime? _nextPayment = DateTime.Today;

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private string _costHint = string.Empty;

    [ObservableProperty]
    private bool _hasCostHint;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && int.TryParse(id?.ToString(), out var subscriptionId))
            _ = LoadAsync(subscriptionId);
    }

    private async Task LoadAsync(int subscriptionId)
    {
        var subscription = await _store.GetSubscriptionAsync(subscriptionId);
        if (subscription is null)
            return;

        _subscription = subscription;
        _loadedPeriod = subscription.Period;
        _loadedNextPayment = BillingSchedule.NextPayment(subscription.BillingAnchor, subscription.Period, DateTime.Today);

        IsNew = false;
        Title = subscription.Name;
        Name = subscription.Name;
        PriceText = subscription.Amount.ToString("0.00", Localizer.Instance.Culture);
        Period = subscription.Period;
        NextPayment = _loadedNextPayment;
        IsActive = subscription.IsActive;
        Appearance.Show(subscription.Icon, subscription.ColorHex);
    }

    private void SelectPeriod(SelectableOption option) => Period = Enum.Parse<BillingPeriod>(option.Value);

    partial void OnPeriodChanged(BillingPeriod value)
    {
        foreach (var option in Periods)
            option.IsSelected = option.Value == value.ToString();
        UpdateCostHint();
    }

    partial void OnPriceTextChanged(string value)
    {
        HasError = false;
        UpdateCostHint();
    }

    partial void OnNameChanged(string value) => HasError = false;

    /// <summary>"That's €161.88 a year" for monthly prices, "That's €5.83 a month" otherwise.</summary>
    private void UpdateCostHint()
    {
        HasCostHint = AmountParser.TryParse(PriceText, out var price) && price > 0;
        if (!HasCostHint)
            return;
        CostHint = Period == BillingPeriod.Monthly
            ? Loc.Format("SubEdit_CostPerYear", _formatter.Format(BillingSchedule.YearlyCost(price, Period)))
            : Loc.Format("SubEdit_CostPerMonth", _formatter.Format(BillingSchedule.MonthlyCost(price, Period)));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!AmountParser.TryParse(PriceText, out var price) || price <= 0)
        {
            ShowError(Loc.Get("SubEdit_InvalidPrice"));
            return;
        }

        var nextPayment = (NextPayment ?? DateTime.Today).Date;
        _subscription.Name = Name;
        _subscription.Icon = Appearance.Icon;
        _subscription.ColorHex = Appearance.ColorHex;
        _subscription.Amount = price;
        _subscription.IsActive = IsActive;
        // Keep the original anchor when the schedule is untouched, so a payment on the 31st stays on the 31st.
        if (IsNew || Period != _loadedPeriod || nextPayment != _loadedNextPayment)
            _subscription.BillingAnchor = nextPayment;
        _subscription.Period = Period;

        try
        {
            await _store.SaveSubscriptionAsync(_subscription);
            Palette.Haptic();
            await Shell.Current.GoToAsync("..");
            if (IsActive)
                await _reminders.EnsurePermissionAsync();
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
            Loc.Format("Edit_DeleteTitle", _subscription.Name),
            Loc.Get("SubEdit_DeleteText"),
            Loc.Get("Edit_DeleteConfirm"));
        if (!confirmed)
            return;

        await _store.DeleteSubscriptionAsync(_subscription.Id);
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
