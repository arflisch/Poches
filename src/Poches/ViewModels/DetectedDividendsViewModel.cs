using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>
/// Dividends found in the quotes and not recorded yet: add the chosen ones as estimates (tax withheld at 30 % under the
/// Belgian option), encode one with the amounts of the statement, or ignore one.
/// </summary>
public sealed partial class DetectedDividendsViewModel(
    BudgetStore store,
    InvestmentService investments,
    AppSettings settings) : ObservableObject, ISheetViewModel
{
    private readonly MoneyFormatter _formatter = new(settings.Currency, culture: Localizer.Instance.Culture);
    private bool _loaded;

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public ObservableCollection<DetectedDividendItem> Items { get; } = [];

    public string Note { get; } = settings.BelgianTax ? Loc.Get("Inv_DetectedNoteBelgian") : Loc.Get("Inv_DetectedNote");

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _addText = Loc.Get("Inv_AddDividends");

    [ObservableProperty]
    private bool _canAdd;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public async Task LoadAsync()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            var snapshot = await investments.LoadAsync(false, CancellationToken.None);
            foreach (var detected in await investments.DetectDividendsAsync(snapshot, CancellationToken.None))
                Items.Add(new DetectedDividendItem(detected, _formatter, Count, IgnoreAsync, EditAsync));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowError(Loc.Get("Inv_DetectFailed"));
        }
        IsLoading = false;
        Count();
    }

    private void Count()
    {
        var chosen = Items.Count(i => i.IsChecked);
        IsEmpty = Items.Count == 0;
        CanAdd = chosen > 0;
        AddText = chosen > 0 ? Loc.Format("Inv_AddDividendsCount", chosen, Loc.Noun(chosen, "Dividend")) : Loc.Get("Inv_AddDividends");
    }

    private Task IgnoreAsync(DetectedDividendItem item)
    {
        settings.IgnoreDividend(item.Detected.Key);
        Items.Remove(item);
        Count();
        Palette.Haptic();
        return Task.CompletedTask;
    }

    private async Task EditAsync(DetectedDividendItem item)
    {
        await Shell.Current.GoToAsync("..");
        await Shell.Current.GoToAsync(Routes.Operation, new Dictionary<string, object>
        {
            ["kind"] = OperationEditViewModel.DividendKind,
            ["detected"] = item.Detected,
        });
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var withholding = settings.BelgianTax ? InvestmentService.BelgianWithholding : 0m;
        var dividends = Items.Where(i => i.IsChecked).Select(i => i.Detected).Select(d => new Dividend
        {
            HoldingId = d.HoldingId,
            Date = d.Date,
            Gross = d.Gross,
            Withholding = Math.Round(d.Gross * withholding, 2),
            Currency = d.FxRate is null ? null : d.Currency,
            FxRateMicros = d.FxRate is { } rate ? Units.ToMicros(rate) : null,
        }).ToList();
        if (dividends.Count == 0)
            return;
        try
        {
            await store.SaveDividendsAsync(dividends, DateTime.Today);
        }
        catch (BudgetException ex)
        {
            ShowError(Loc.Error(ex));
            return;
        }
        SuccessToast.Show(Loc.Format("Inv_DividendsAdded", dividends.Count, Loc.Noun(dividends.Count, "Dividend")));
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

public sealed partial class DetectedDividendItem : ObservableObject
{
    private readonly Action _changed;

    public DetectedDividendItem(DetectedDividend detected, MoneyFormatter formatter, Action changed,
        Func<DetectedDividendItem, Task> ignore, Func<DetectedDividendItem, Task> edit)
    {
        Detected = detected;
        _changed = changed;
        Line = detected.Line;
        var foreign = detected.FxRate is not null;
        DetailText = Loc.Format("Inv_DetectedDetail", Loc.Date(detected.Date, "d MMM yyyy"), InvestmentText.Quantity(detected.Units),
            $"{InvestmentText.Editable(detected.PerShare, 4)} {detected.Currency}");
        GrossText = foreign ? $"{InvestmentText.Editable(detected.Gross, 2)} {detected.Currency}" : formatter.Format(detected.Gross);
        BaseText = foreign ? Loc.Format("Inv_AboutBase", formatter.Format(detected.GrossBase)) : Loc.Get("Inv_GrossShort");
        IgnoreCommand = new AsyncRelayCommand(() => ignore(this));
        EditCommand = new AsyncRelayCommand(() => edit(this));
    }

    public DetectedDividend Detected { get; }

    public string Line { get; }

    public string DetailText { get; }

    public string GrossText { get; }

    public string BaseText { get; }

    [ObservableProperty]
    private bool _isChecked = true;

    partial void OnIsCheckedChanged(bool value) => _changed();

    [RelayCommand]
    private void Toggle() => IsChecked = !IsChecked;

    public IAsyncRelayCommand IgnoreCommand { get; }

    public IAsyncRelayCommand EditCommand { get; }
}
