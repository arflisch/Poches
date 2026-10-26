using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>
/// The monthly plan and the goal of the investments, the assumptions of the projection and, under the Belgian option,
/// the capital gains tax rate and yearly exemption; under the US option the federal rates, the net investment income
/// tax, the state rate and the yearly loss deduction.
/// </summary>
public sealed partial class InvestmentPlanViewModel(BudgetStore store, AppSettings settings) : ObservableObject, ISheetViewModel
{
    private InvestmentSettings _plan = new();
    private bool _loaded;

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public string Currency => settings.Currency;

    public bool IsBelgian => settings.BelgianTax;

    public bool IsUs => settings.UsTax;

    /// <summary>The net investment income tax rate, when the income is above its threshold.</summary>
    public const decimal NiitRate = 0.038m;

    [ObservableProperty]
    private string _shortTermText = "22";

    [ObservableProperty]
    private string _longTermText = "15";

    [ObservableProperty]
    private bool _niit;

    [ObservableProperty]
    private string _stateRateText = "0";

    [ObservableProperty]
    private string _lossLimitText = "3000";

    [ObservableProperty]
    private string _monthlyText = string.Empty;

    [ObservableProperty]
    private string _dayText = "5";

    [ObservableProperty]
    private string _goalText = string.Empty;

    [ObservableProperty]
    private DateTime? _goalDate = DateTime.Today.AddYears(15);

    public DateTime MinimumGoalDate => DateTime.Today.AddMonths(1);

    [ObservableProperty]
    private string _returnText = "5";

    [ObservableProperty]
    private string _volatilityText = "15";

    [ObservableProperty]
    private string _taxRateText = "10";

    [ObservableProperty]
    private string _exemptionText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public async Task LoadAsync()
    {
        if (_loaded)
            return;
        _loaded = true;
        _plan = await store.GetInvestmentSettingsAsync();
        MonthlyText = _plan.Monthly is { } monthly ? InvestmentText.Editable(monthly, 2) : string.Empty;
        DayText = _plan.MonthlyDay.ToString(Localizer.Instance.Culture);
        GoalText = _plan.Goal is { } goal ? InvestmentText.Editable(goal, 2) : string.Empty;
        GoalDate = _plan.GoalDate ?? DateTime.Today.AddYears(15);
        ReturnText = InvestmentText.Editable(_plan.ExpectedReturn * 100, 2);
        VolatilityText = InvestmentText.Editable(_plan.Volatility * 100, 2);
        TaxRateText = InvestmentText.Editable(_plan.CapitalGainsRate * 100, 2);
        ExemptionText = InvestmentText.Editable(_plan.Exemption, 2);
        ShortTermText = InvestmentText.Editable(_plan.UsShortTermRate * 100, 2);
        LongTermText = InvestmentText.Editable(_plan.UsLongTermRate * 100, 2);
        Niit = _plan.UsNiitRate > 0;
        StateRateText = InvestmentText.Editable(_plan.UsStateRate * 100, 2);
        LossLimitText = InvestmentText.Editable(_plan.UsLossLimit, 2);
    }

    private static bool TryAmount(string text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!AmountParser.TryParse(text, out var parsed) || parsed < 0)
            return false;
        value = parsed;
        return true;
    }

    private static bool TryPercent(string text, decimal min, decimal max, out decimal value) =>
        AmountParser.TryParseDecimal(text, 4, out value) && value >= min && value <= max;

    [RelayCommand]
    private async Task SaveAsync()
    {
        HasError = false;
        if (!TryAmount(MonthlyText, out var monthly) || !int.TryParse(DayText, Localizer.Instance.Culture, out var day) || day is < 1 or > 28)
        {
            ShowError(Loc.Get("Error_InvalidPlan"));
            return;
        }
        if (!TryAmount(GoalText, out var goal) || !TryPercent(ReturnText, -50, 50, out var expected) || !TryPercent(VolatilityText, 0, 100, out var volatility))
        {
            ShowError(Loc.Get("Error_InvalidGoal"));
            return;
        }
        if (!TryPercent(TaxRateText, 0, 50, out var taxRate) || !TryAmount(ExemptionText, out var exemption))
        {
            ShowError(Loc.Get("Error_InvalidTaxSettings"));
            return;
        }

        if (!TryPercent(ShortTermText, 0, 50, out var shortTerm) || !TryPercent(LongTermText, 0, 50, out var longTerm)
            || !TryPercent(StateRateText, 0, 50, out var state) || !TryAmount(LossLimitText, out var lossLimit))
        {
            ShowError(Loc.Get("Error_InvalidTaxSettings"));
            return;
        }

        _plan.UsShortTermRateMicros = Units.ToMicros(shortTerm / 100);
        _plan.UsLongTermRateMicros = Units.ToMicros(longTerm / 100);
        _plan.UsNiitRateMicros = Niit ? Units.ToMicros(NiitRate) : 0;
        _plan.UsStateRateMicros = Units.ToMicros(state / 100);
        _plan.UsLossLimitCents = Money.ToCents(lossLimit ?? 0);
        _plan.MonthlyCents = monthly is > 0 ? Money.ToCents(monthly.Value) : null;
        _plan.MonthlyDay = day;
        _plan.GoalCents = goal is > 0 ? Money.ToCents(goal.Value) : null;
        _plan.GoalDate = goal is > 0 ? (GoalDate ?? DateTime.Today.AddYears(15)).Date : null;
        _plan.ExpectedReturnMicros = Units.ToMicros(expected / 100);
        _plan.VolatilityMicros = Units.ToMicros(volatility / 100);
        _plan.CapitalGainsRateMicros = Units.ToMicros(taxRate / 100);
        _plan.ExemptionCents = Money.ToCents(exemption ?? 0);
        try
        {
            await store.SaveInvestmentSettingsAsync(_plan, DateTime.Today);
            SuccessToast.Show(Loc.Get("Toast_Saved"));
            await Shell.Current.GoToAsync("..");
        }
        catch (BudgetException ex)
        {
            ShowError(Loc.Error(ex));
        }
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
