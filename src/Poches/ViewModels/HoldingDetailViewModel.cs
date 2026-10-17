using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Controls;
using Poches.Core.Formatting;
using Poches.Core.Investments;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>One investment line: its value, average cost, and the performance of each purchase since its date.</summary>
public sealed partial class HoldingDetailViewModel(InvestmentService investments, AppSettings settings)
    : ReloadingViewModel, IQueryAttributable
{
    private int _holdingId;

    public ObservableCollection<PurchaseItemViewModel> Purchases { get; } = [];

    public Func<double, string> AxisFormatter => value => InvestmentText.Price((decimal)Math.Round(value, 2), settings.Formatter);

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private Color _color = Colors.Transparent;

    [ObservableProperty]
    private Brush _heroBrush = new SolidColorBrush(Colors.Transparent);

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _valueText = string.Empty;

    [ObservableProperty]
    private string _gainText = string.Empty;

    [ObservableProperty]
    private string _investedText = string.Empty;

    [ObservableProperty]
    private string _averageCostText = string.Empty;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private string _priceCaption = string.Empty;

    [ObservableProperty]
    private string _xirrText = string.Empty;

    [ObservableProperty]
    private string _weightText = string.Empty;

    [ObservableProperty]
    private PurchaseChartData? _chart;

    [ObservableProperty]
    private bool _hasPurchases;

    [ObservableProperty]
    private bool _isEmpty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && int.TryParse(id?.ToString(), out var holdingId))
        {
            _holdingId = holdingId;
            RequestReload();
        }
    }

    protected override async Task LoadCoreAsync()
    {
        if (_holdingId == 0)
            return;
        var snapshot = await investments.LoadAsync(false, CancellationToken.None);
        var holding = snapshot.Holdings.FirstOrDefault(h => h.Id == _holdingId);
        if (holding is null)
        {
            // Deleted from its edit sheet: leave the screen.
            if (Shell.Current.CurrentPage?.BindingContext == this)
                await Shell.Current.GoToAsync("..");
            return;
        }

        var formatter = settings.Formatter;
        Name = holding.Name;
        Icon = HoldingTypes.Icon(holding.Type);
        Color = Color.FromArgb(holding.ColorHex);
        HeroBrush = Palette.HeroBrush(Color);
        Subtitle = string.Join(" · ", new[] { HoldingTypes.Name(holding.Type), holding.Account, holding.Symbol ?? holding.Code }.Where(p => !string.IsNullOrEmpty(p)));

        var performance = snapshot.Performance.Holdings.FirstOrDefault(h => h.Holding.Id == _holdingId);
        HasPurchases = performance is not null;
        IsEmpty = performance is null;
        Purchases.Clear();
        if (performance is null)
        {
            ValueText = formatter.Format(0);
            GainText = Loc.Get("Inv_NoPurchaseYet");
            Chart = null;
            return;
        }

        ValueText = formatter.Format(performance.Value);
        GainText = $"{formatter.FormatSigned(performance.Gain)} · {InvestmentText.SignedPercent(performance.Return)}";
        InvestedText = formatter.Format(performance.Invested);
        AverageCostText = InvestmentText.Price(performance.AverageCost, formatter);
        PriceText = InvestmentText.Price(performance.Price, formatter);
        PriceCaption = performance.Source switch
        {
            PriceSource.Quote => Loc.Format("Inv_PriceOf", Loc.Date(performance.PriceDate, "d MMM")),
            PriceSource.Manual => Loc.Format("Inv_PriceManual", Loc.Date(performance.PriceDate, "d MMM")),
            _ => Loc.Get("Inv_PriceLastPurchase"),
        };
        XirrText = performance.Xirr is { } xirr ? InvestmentText.SignedPercent(xirr) : "—";
        WeightText = InvestmentText.Percent(performance.Weight);

        foreach (var purchase in performance.Purchases.OrderByDescending(p => p.Purchase.Date).ThenByDescending(p => p.Purchase.Id))
            Purchases.Add(new PurchaseItemViewModel(purchase, formatter, OpenPurchaseAsync));

        Chart = new PurchaseChartData(
            performance.Purchases.Select(p => new PurchaseDot(p.Purchase.Date, (double)p.Purchase.UnitPrice, (double)p.Invested, p.Gain >= 0)).ToList(),
            (double)performance.Price,
            (double)performance.AverageCost,
            performance.PriceDate);
    }

    private Task OpenPurchaseAsync(int purchaseId) => Shell.Current.GoToAsync($"{Routes.Purchase}?id={purchaseId}");

    [RelayCommand]
    private Task AddPurchaseAsync() => Shell.Current.GoToAsync($"{Routes.Purchase}?holdingId={_holdingId}");

    [RelayCommand]
    private Task EditAsync() => Shell.Current.GoToAsync($"{Routes.EditHolding}?id={_holdingId}");

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");
}

public sealed class PurchaseItemViewModel
{
    public PurchaseItemViewModel(PurchasePerformance performance, MoneyFormatter formatter, Func<int, Task> open)
    {
        var purchase = performance.Purchase;
        Id = purchase.Id;
        DateText = Loc.Date(purchase.Date, "d MMM yyyy");
        DetailText = $"{InvestmentText.Quantity(purchase.Quantity)} × {InvestmentText.Price(purchase.UnitPrice, formatter)}";
        AmountsText = $"{formatter.Format(performance.Invested)} → {formatter.Format(performance.Value)}";
        ReturnText = InvestmentText.SignedPercent(performance.Return);
        GainText = formatter.FormatSigned(performance.Gain);
        IsGain = performance.Gain > 0.004m;
        IsLoss = performance.Gain < -0.004m;
        AnnualizedText = performance.Annualized is { } rate
            ? Loc.Format("Inv_PerYear", InvestmentText.SignedPercent(rate))
            : Loc.Format("Inv_HeldDays", performance.Days);
        OpenCommand = new AsyncRelayCommand(() => open(Id));
    }

    public int Id { get; }

    public string DateText { get; }

    public string DetailText { get; }

    public string AmountsText { get; }

    public string ReturnText { get; }

    public string GainText { get; }

    public bool IsGain { get; }

    public bool IsLoss { get; }

    public string AnnualizedText { get; }

    public IAsyncRelayCommand OpenCommand { get; }
}
