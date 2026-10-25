using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Formatting;
using Poches.Core.Investments;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>
/// The currency of an operation and, for a foreign one, the rate of the bank statement (units of that currency for
/// one unit of the portfolio currency), with the market rate of the day offered.
/// </summary>
public sealed partial class CurrencyFieldViewModel(InvestmentService investments, AppSettings settings, IDialogService dialogs) : ObservableObject
{
    private decimal? _market;
    private DateTime _date = DateTime.Today;
    private int _lookup;

    /// <summary>Raised when the currency or the rate changes, for the previews.</summary>
    public event Action? Changed;

    public string BaseCode => settings.CurrencyCode;

    [ObservableProperty]
    private string _code = settings.CurrencyCode;

    [ObservableProperty]
    private bool _isForeign;

    [ObservableProperty]
    private string _rateText = string.Empty;

    [ObservableProperty]
    private string _rateLabel = string.Empty;

    [ObservableProperty]
    private string _marketHint = string.Empty;

    [ObservableProperty]
    private bool _hasMarketHint;

    /// <summary>What amounts are typed in: the currency code when foreign, the portfolio's symbol otherwise.</summary>
    public string AmountUnit => IsForeign ? Code : settings.Currency;

    public void Load(string? currency, decimal? rate)
    {
        Code = currency ?? BaseCode;
        RateText = rate is { } r ? InvestmentText.Editable(r, 6) : string.Empty;
    }

    /// <summary>The operation's date: the market rate offered is that day's.</summary>
    public Task SetDateAsync(DateTime date)
    {
        _date = date.Date;
        return LookUpMarketAsync();
    }

    partial void OnCodeChanged(string value)
    {
        IsForeign = value != BaseCode;
        RateLabel = Loc.Format("Inv_RateLabel", value, BaseCode);
        OnPropertyChanged(nameof(AmountUnit));
        Changed?.Invoke();
        _ = LookUpMarketAsync();
    }

    partial void OnRateTextChanged(string value) => Changed?.Invoke();

    private async Task LookUpMarketAsync()
    {
        var lookup = ++_lookup;
        _market = null;
        HasMarketHint = false;
        if (!IsForeign)
            return;
        decimal? market = null;
        try
        {
            market = await investments.MarketRateAsync(Code, _date, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only a hint.
        }
        if (lookup != _lookup || market is not > 0)
            return;
        _market = market;
        MarketHint = Loc.Format("Inv_MarketRate", Loc.Date(_date, "d MMM"), InvestmentText.Editable(market.Value, 4));
        HasMarketHint = true;
        if (string.IsNullOrWhiteSpace(RateText))
            RateText = InvestmentText.Editable(market.Value, 6);
    }

    /// <summary>The rate used for previews: 1 when not foreign or not readable yet.</summary>
    public decimal PreviewRate => IsForeign && AmountParser.TryParseDecimal(RateText, 6, out var rate) && rate > 0 ? rate : 1;

    /// <summary>
    /// Reads the currency and rate. A statement may quote the rate either way round: it is turned around when the
    /// inverse is closer to the market rate.
    /// </summary>
    public bool TryRead(out string? currency, out decimal? rate)
    {
        currency = null;
        rate = null;
        if (!IsForeign)
            return true;
        if (!AmountParser.TryParseDecimal(RateText, 6, out var typed) || typed <= 0 || typed > Operations.MaxFxRate)
            return false;
        currency = Code;
        rate = Math.Round(FxTools.OrientRate(typed, _market) ?? typed, 6);
        return true;
    }

    [RelayCommand]
    private async Task ChooseAsync()
    {
        var choice = await dialogs.ChooseAsync(Loc.Get("Inv_Currency"), null, Operations.Currencies.ToArray());
        if (choice is null || choice == Code)
            return;
        RateText = string.Empty;
        Code = choice;
    }

    [RelayCommand]
    private void UseMarket()
    {
        if (_market is { } market)
            RateText = InvestmentText.Editable(market, 6);
    }
}
