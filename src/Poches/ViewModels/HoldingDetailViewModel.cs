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
public sealed partial class HoldingDetailViewModel(InvestmentService investments, AppSettings settings, IDialogService dialogs)
    : ReloadingViewModel, IQueryAttributable
{
    private int _holdingId;
    private IReadOnlyList<PurchaseDot> _dots = [];
    private double _current, _averageCost;
    private DateTime _priceDate;
    private int? _year;

    public ObservableCollection<PurchaseItemViewModel> Purchases { get; } = [];

    public ObservableCollection<OperationItemViewModel> Sales { get; } = [];

    public ObservableCollection<OperationItemViewModel> DividendItems { get; } = [];

    [ObservableProperty]
    private bool _hasSales;

    [ObservableProperty]
    private bool _hasDividends;

    [ObservableProperty]
    private bool _hasRealized;

    [ObservableProperty]
    private string _realizedText = string.Empty;

    [ObservableProperty]
    private string _dividendsText = string.Empty;

    [ObservableProperty]
    private bool _isOpen = true;

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

    /// <summary>One chip per year with purchases, newest first, when they span more than a year.</summary>
    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _years = [];

    [ObservableProperty]
    private bool _hasYears;

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
        Sales.Clear();
        DividendItems.Clear();
        HasSales = HasDividends = HasRealized = false;
        if (performance is null)
        {
            ValueText = formatter.Format(0);
            GainText = Loc.Get("Inv_NoPurchaseYet");
            Chart = null;
            Years = [];
            HasYears = false;
            return;
        }

        IsOpen = !performance.Closed;
        ValueText = formatter.Format(performance.Value);
        GainText = performance.Closed
            ? Loc.Get("Inv_ClosedLine")
            : $"{formatter.FormatSigned(performance.Gain)} · {InvestmentText.SignedPercent(performance.Return)}";
        HasRealized = performance.Sales.Count > 0 || performance.DividendsReceived.Count > 0;
        RealizedText = formatter.FormatSigned(performance.Realized);
        DividendsText = formatter.Format(performance.Dividends);
        foreach (var sale in performance.Sales.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id))
        {
            var gain = performance.Slices.Where(x => x.Sale.Id == sale.Id).Sum(x => x.Gain);
            Sales.Add(new OperationItemViewModel(sale.Id, Loc.Date(sale.Date, "d MMM yyyy"),
                $"{InvestmentText.Quantity(sale.Quantity)} × {Native(sale.UnitPrice, sale.Currency, formatter)}",
                formatter.Format(sale.Proceeds), Loc.Format("Inv_SaleGain", formatter.FormatSigned(gain)), gain > 0.004m, gain < -0.004m,
                id => Shell.Current.GoToAsync($"{Routes.Operation}?kind={OperationEditViewModel.SaleKind}&id={id}")));
        }
        HasSales = Sales.Count > 0;
        foreach (var dividend in performance.DividendsReceived.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id))
        {
            DividendItems.Add(new OperationItemViewModel(dividend.Id, Loc.Date(dividend.Date, "d MMM yyyy"),
                Loc.Format("Inv_DividendDetail", Native(dividend.Gross, dividend.Currency, formatter), Native(dividend.Withholding, dividend.Currency, formatter)),
                formatter.Format(dividend.Net), Loc.Get("Inv_Net"), dividend.Net > 0, false,
                id => Shell.Current.GoToAsync($"{Routes.Operation}?kind={OperationEditViewModel.DividendKind}&id={id}")));
        }
        HasDividends = DividendItems.Count > 0;
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

        _dots = performance.Purchases.Select(p => new PurchaseDot(p.Purchase.Date, (double)p.Purchase.UnitPrice, (double)p.Invested, p.Gain >= 0)).ToList();
        _current = (double)performance.Price;
        _averageCost = (double)performance.AverageCost;
        _priceDate = performance.PriceDate;

        // Years of monthly purchases would pile up dozens of dots: the chart shows one year at a time.
        var years = _dots.Select(d => d.Date.Year).Distinct().OrderDescending().ToList();
        if (_year is not { } kept || !years.Contains(kept))
            _year = years[0];
        Years = years.Count > 1
            ? years.Select((y, i) => new SelectableOption(y.ToString(System.Globalization.CultureInfo.InvariantCulture), i, years.Count, o => ShowYear(int.Parse(o.Value, System.Globalization.CultureInfo.InvariantCulture)))).ToList()
            : [];
        HasYears = Years.Count > 0;
        ShowYear(_year.Value);
    }

    private void ShowYear(int year)
    {
        _year = year;
        foreach (var option in Years)
            option.IsSelected = option.Value == year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var dots = HasYears ? _dots.Where(d => d.Date.Year == year).ToList() : _dots;
        // The current year runs to today's price; a past one stops at its last purchase.
        var end = year == _priceDate.Year || !HasYears ? _priceDate : dots.Max(d => d.Date);
        Chart = new PurchaseChartData(dots, _current, _averageCost, end);
    }

    private static string Native(decimal amount, string? currency, MoneyFormatter formatter) =>
        currency is null ? InvestmentText.Price(amount, formatter) : $"{InvestmentText.Editable(amount, 4)} {currency}";

    private Task OpenPurchaseAsync(int purchaseId) => Shell.Current.GoToAsync($"{Routes.Purchase}?id={purchaseId}");

    [RelayCommand]
    private Task AddPurchaseAsync() => Shell.Current.GoToAsync($"{Routes.Purchase}?holdingId={_holdingId}");

    /// <summary>A purchase, a sale (while units are held) or a dividend of this line.</summary>
    [RelayCommand]
    private async Task AddOperationAsync()
    {
        var purchase = Loc.Get("Inv_AddPurchase");
        var sale = Loc.Get("Inv_AddSale");
        var dividend = Loc.Get("Inv_AddDividend");
        var choice = await dialogs.ChooseAsync(Loc.Get("Inv_AddWhat"), null, IsOpen ? [purchase, sale, dividend] : [purchase, dividend]);
        if (choice == purchase)
            await AddPurchaseAsync();
        else if (choice == sale)
            await Shell.Current.GoToAsync($"{Routes.Operation}?kind={OperationEditViewModel.SaleKind}&holdingId={_holdingId}");
        else if (choice == dividend)
            await Shell.Current.GoToAsync($"{Routes.Operation}?kind={OperationEditViewModel.DividendKind}&holdingId={_holdingId}");
    }

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
        DetailText = $"{InvestmentText.Quantity(purchase.Quantity)} × {(purchase.Currency is { } currency ? $"{InvestmentText.Editable(purchase.UnitPrice, 4)} {currency}" : InvestmentText.Price(purchase.UnitPrice, formatter))}";
        if (performance.SoldQuantity > 0)
            DetailText += " · " + Loc.Format("Inv_SoldUnits", InvestmentText.Quantity(performance.SoldQuantity));
        AmountsText = performance.SoldOut
            ? Loc.Format("Inv_SoldOutRealized", formatter.FormatSigned(performance.Realized))
            : $"{formatter.Format(performance.Invested)} → {formatter.Format(performance.Value)}";
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

/// <summary>A sale or a dividend in the line's history.</summary>
public sealed class OperationItemViewModel(int id, string dateText, string detailText, string amountText, string captionText, bool isGain, bool isLoss, Func<int, Task> open)
{
    public string DateText { get; } = dateText;

    public string DetailText { get; } = detailText;

    public string AmountText { get; } = amountText;

    public string CaptionText { get; } = captionText;

    public bool IsGain { get; } = isGain;

    public bool IsLoss { get; } = isLoss;

    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => open(id));
}
