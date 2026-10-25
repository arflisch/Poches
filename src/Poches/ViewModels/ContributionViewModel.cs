using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Formatting;
using Poches.Core.Investments;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>
/// Splits the next contribution between the lines so they move towards their target weights, without selling.
/// </summary>
public sealed partial class ContributionViewModel(InvestmentService investments, AppSettings settings) : ObservableObject, ISheetViewModel
{
    private readonly MoneyFormatter _formatter = new(settings.Currency, culture: Localizer.Instance.Culture);
    private IReadOnlyList<HoldingPerformance> _lines = [];
    private bool _loaded;

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    public string Currency => settings.Currency;

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ContributionItem> _items = [];

    [ObservableProperty]
    private bool _hasTargets;

    [ObservableProperty]
    private bool _hasNoTarget;

    [ObservableProperty]
    private string _summary = string.Empty;

    public async Task LoadAsync()
    {
        if (_loaded)
            return;
        _loaded = true;
        var snapshot = await investments.LoadAsync(false, CancellationToken.None);
        _lines = snapshot.Performance.Open.ToList();
        HasTargets = _lines.Any(l => l.Holding.Target is > 0);
        HasNoTarget = !HasTargets;
        AmountText = snapshot.Plan.Monthly is { } monthly ? InvestmentText.Editable(monthly, 2) : string.Empty;
        Update();
    }

    partial void OnAmountTextChanged(string value) => Update();

    private void Update()
    {
        var amount = AmountParser.TryParse(AmountText, out var parsed) && parsed > 0 ? parsed : 0;
        var parts = Planning.NextContribution(
            _lines.Select(l => new ContributionLine(l.Holding.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), l.Value, l.Holding.Target ?? 0)).ToList(),
            amount).ToDictionary(p => p.Id, p => p.Amount);
        var total = _lines.Sum(l => l.Value);
        var after = total + amount;
        Items = _lines.Where(l => l.Holding.Target is > 0)
            .OrderByDescending(l => l.Holding.Target)
            .Select(l =>
            {
                var part = parts.GetValueOrDefault(l.Holding.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return new ContributionItem(
                    l.Holding.Name,
                    Color.FromArgb(l.Holding.ColorHex),
                    Loc.Format("Inv_WeightToTarget", InvestmentText.Percent(total > 0 ? (double)(l.Value / total) : 0), InvestmentText.Percent((double)l.Holding.Target!.Value),
                        InvestmentText.Percent(after > 0 ? (double)((l.Value + part) / after) : 0)),
                    part > 0 ? _formatter.Format(part) : "—");
            })
            .ToList();
        Summary = amount > 0 ? Loc.Format("Inv_ContributionSummary", _formatter.Format(amount)) : Loc.Get("Inv_ContributionEnterAmount");
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");
}

public sealed record ContributionItem(string Name, Color Color, string WeightText, string AmountText);
