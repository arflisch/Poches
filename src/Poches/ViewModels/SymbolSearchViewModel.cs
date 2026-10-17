using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Core.Investments;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

/// <summary>Finds a listing by name, ticker or ISIN on Yahoo Finance and keeps the chosen symbol.</summary>
public sealed partial class SymbolSearchViewModel : ObservableObject
{
    private readonly InvestmentService _investments;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _search;
    private bool _showing; // set while Show fills the query, so it doesn't start a search

    public SymbolSearchViewModel(InvestmentService investments, AppSettings settings)
    {
        _investments = investments;
        _settings = settings;
        IsOnline = settings.OnlineQuotes;
    }

    /// <summary>Called when the user picks a result, to prefill the name and type of a new line.</summary>
    public Action<SymbolMatch>? Picked { get; set; }

    public ObservableCollection<SymbolResult> Results { get; } = [];

    /// <summary>Searching needs online quotes; otherwise the picker explains how to turn them on.</summary>
    public bool IsOnline { get; }

    public bool IsOffline => !IsOnline;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _hasMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSymbol), nameof(IsSearchVisible))]
    private string? _symbol;

    [ObservableProperty]
    private string _symbolCaption = string.Empty;

    /// <summary>The latest quote of the chosen symbol, once known.</summary>
    [ObservableProperty]
    private decimal? _currentPrice;

    public bool HasSymbol => Symbol is not null;

    public bool IsSearchVisible => Symbol is null && IsOnline;

    /// <summary>Shows an existing symbol without searching.</summary>
    public void Show(string? symbol, string? query)
    {
        Symbol = symbol;
        SymbolCaption = string.Empty;
        _showing = true;
        Query = query ?? string.Empty;
        _showing = false;
        if (symbol is not null)
            _ = DescribeAsync(symbol, null);
    }

    partial void OnQueryChanged(string value)
    {
        if (!_showing)
            _ = SearchAsync(value);
    }

    private async Task SearchAsync(string query)
    {
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var token = _search.Token;
        if (query.Trim().Length < 2 || !IsOnline)
        {
            Results.Clear();
            ShowMessage(string.Empty);
            return;
        }

        try
        {
            await Task.Delay(350, token);
            ShowMessage(Loc.Get("Inv_Searching"));
            var matches = await _investments.SearchAsync(query, token);
            if (token.IsCancellationRequested)
                return;
            Results.Clear();
            foreach (var match in matches)
                Results.Add(new SymbolResult(match, Pick));
            ShowMessage(matches.Count == 0 ? Loc.Get("Inv_NoResult") : string.Empty);
        }
        catch (OperationCanceledException)
        {
            // A newer search replaced this one.
        }
        catch (QuoteSourceException)
        {
            Results.Clear();
            ShowMessage(Loc.Get("Inv_SearchUnavailable"));
        }
    }

    private void Pick(SymbolMatch match)
    {
        Results.Clear();
        ShowMessage(string.Empty);
        Symbol = match.Symbol;
        Picked?.Invoke(match);
        _ = DescribeAsync(match.Symbol, $"{HoldingTypes.ShortName(match.Name)} · {match.Exchange}");
        Palette.Haptic();
    }

    private async Task DescribeAsync(string symbol, string? detail)
    {
        SymbolCaption = detail ?? string.Empty;
        CurrentPrice = null;
        try
        {
            var quote = await _investments.PreviewAsync(symbol, CancellationToken.None);
            if (Symbol != symbol)
                return;
            if (quote is { Status: QuoteStatus.Ok, Price: { } price })
            {
                CurrentPrice = price;
                var text = Loc.Format("Inv_CurrentPrice", InvestmentText.Price(price, new Core.Formatting.MoneyFormatter(_settings.Currency, culture: Localizer.Instance.Culture)));
                SymbolCaption = detail is null ? text : $"{detail} · {text}";
            }
            else if (quote is { Status: QuoteStatus.NotFound })
            {
                SymbolCaption = Loc.Get("Inv_SymbolNotFound");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The price preview is only informative.
        }
    }

    [RelayCommand]
    private void Clear()
    {
        Symbol = null;
        SymbolCaption = string.Empty;
        CurrentPrice = null;
        if (Query.Trim().Length >= 2)
            _ = SearchAsync(Query);
    }

    private void ShowMessage(string text)
    {
        Message = text;
        HasMessage = text.Length > 0;
    }
}

public sealed class SymbolResult(SymbolMatch match, Action<SymbolMatch> pick)
{
    public string Symbol { get; } = match.Symbol;

    public string Name { get; } = match.Name;

    public string Caption { get; } = $"{match.Exchange} · {HoldingTypes.Name(HoldingTypes.FromKind(match.Kind))}";

    public IRelayCommand PickCommand { get; } = new RelayCommand(() => pick(match));
}
