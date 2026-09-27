using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Core.Pro;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>Splits an income between the pockets by percentage (Poches Pro). The percentages are remembered.</summary>
public sealed partial class SplitViewModel : ObservableObject, ISheetViewModel
{
    private readonly BudgetStore _store;
    private readonly MoneyFormatter _formatter;
    private bool _loading;

    public SplitViewModel(BudgetStore store, AppSettings settings)
    {
        _store = store;
        _formatter = new MoneyFormatter(settings.Currency, culture: Localizer.Instance.Culture);
        Currency = settings.Currency;
        AmountPlaceholder = 2400m.ToString("0.00", Localizer.Instance.Culture);
        _ = LoadAsync();
    }

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public ObservableCollection<SplitRowViewModel> Rows { get; } = [];

    public string Currency { get; }

    public string AmountPlaceholder { get; }

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private DateTime? _date = DateTime.Today;

    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>"Total: 85 %".</summary>
    [ObservableProperty]
    private string _totalText = string.Empty;

    /// <summary>"Left aside: €360.00".</summary>
    [ObservableProperty]
    private string _unallocatedText = string.Empty;

    [ObservableProperty]
    private bool _hasUnallocated;

    [ObservableProperty]
    private bool _isOverHundred;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    private async Task LoadAsync()
    {
        _loading = true;
        var rules = (await _store.GetSplitRulesAsync()).ToDictionary(r => r.PocketId, r => r.BasisPoints);
        foreach (var summary in await _store.GetPocketSummariesAsync())
        {
            var points = rules.GetValueOrDefault(summary.Pocket.Id);
            Rows.Add(new SplitRowViewModel(summary.Pocket, points > 0 ? FormatPercent(points) : string.Empty, Recompute));
        }
        _loading = false;
        Recompute();
    }

    partial void OnAmountTextChanged(string value) => Recompute();

    /// <summary>Shows what each pocket would receive, as the amount and the percentages are typed.</summary>
    private void Recompute()
    {
        if (_loading)
            return;
        HasError = false;

        var totalPoints = Rows.Sum(r => r.BasisPoints);
        IsOverHundred = totalPoints > SalarySplit.WholeIncome;
        TotalText = Loc.Format("Split_Total", FormatPercent(totalPoints));

        var hasAmount = AmountParser.TryParse(AmountText, out var amount) && amount > 0;
        if (!hasAmount || IsOverHundred)
        {
            foreach (var row in Rows)
                row.AmountPreview = string.Empty;
            HasUnallocated = false;
            return;
        }

        var result = SalarySplit.Compute(Money.ToCents(amount), Rows.Select(r => r.ToRule()).ToList());
        var byPocket = result.Shares.ToDictionary(s => s.PocketId, s => s.Amount);
        foreach (var row in Rows)
            row.AmountPreview = byPocket.TryGetValue(row.PocketId, out var share) ? _formatter.Format(share) : string.Empty;
        HasUnallocated = result.UnallocatedCents > 0;
        UnallocatedText = Loc.Format("Split_Unallocated", _formatter.Format(result.Unallocated));
    }

    [RelayCommand]
    private async Task SplitAsync()
    {
        if (!AmountParser.TryParse(AmountText, out var amount) || amount <= 0)
        {
            ShowError(Loc.Get("Movement_EnterAmount"));
            return;
        }
        if (Rows.Any(r => !r.IsValid))
        {
            ShowError(Loc.Get("Split_InvalidPercent"));
            return;
        }
        if (Rows.All(r => r.BasisPoints == 0))
        {
            ShowError(Loc.Get("Error_NoSplitRules"));
            return;
        }

        try
        {
            await _store.SaveSplitRulesAsync(Rows.Select(r => r.ToRule()));
            var day = Date ?? DateTime.Today;
            var date = day.Date == DateTime.Today ? DateTime.Now : day.Date.AddHours(12);
            await _store.ApplySplitAsync(amount, date, string.IsNullOrWhiteSpace(Note) ? Loc.Get("Split_DefaultNote") : Note);
            SuccessToast.Show(Loc.Get("Split_Done"));
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

    private static string FormatPercent(int basisPoints) =>
        (basisPoints / 100m).ToString("0.##", Localizer.Instance.Culture);
}

/// <summary>A pocket and the share of the income it receives.</summary>
public sealed partial class SplitRowViewModel(Pocket pocket, string percentText, Action changed) : ObservableObject
{
    public int PocketId { get; } = pocket.Id;

    public string Name { get; } = pocket.Name;

    public string Icon { get; } = pocket.Icon;

    public Color SoftColor { get; } = Palette.Soft(Color.FromArgb(pocket.ColorHex));

    [ObservableProperty]
    private string _percentText = percentText;

    /// <summary>What the pocket receives from the income being typed.</summary>
    [ObservableProperty]
    private string _amountPreview = string.Empty;

    /// <summary>Empty counts as 0 %; otherwise between 0 and 100.</summary>
    public bool IsValid => string.IsNullOrWhiteSpace(PercentText)
        || (AmountParser.TryParse(PercentText, out var percent) && percent <= 100);

    public int BasisPoints =>
        !string.IsNullOrWhiteSpace(PercentText) && AmountParser.TryParse(PercentText, out var percent) && percent <= 100
            ? (int)Math.Round(percent * 100, MidpointRounding.AwayFromZero)
            : 0;

    public SplitRule ToRule() => new() { PocketId = PocketId, BasisPoints = BasisPoints };

    partial void OnPercentTextChanged(string value) => changed();
}
