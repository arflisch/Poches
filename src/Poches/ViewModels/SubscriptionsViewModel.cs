using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Controls;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Core.Subscriptions;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

public sealed partial class SubscriptionsViewModel(BudgetStore store, AppSettings settings)
    : ReloadingViewModel, IBreakdown
{
    public ObservableCollection<SubscriptionItemViewModel> Active { get; } = [];

    public ObservableCollection<SubscriptionItemViewModel> Inactive { get; } = [];

    [ObservableProperty]
    private string _todayText = string.Empty;

    [ObservableProperty]
    private string _monthlyPrefix = string.Empty;

    [ObservableProperty]
    private string _monthlyWhole = "0";

    [ObservableProperty]
    private string _monthlyFraction = string.Empty;

    [ObservableProperty]
    private string _yearlyText = string.Empty;

    [ObservableProperty]
    private string _privacyIcon = Icons.Eye;

    [ObservableProperty]
    private bool _hasNext;

    [ObservableProperty]
    private string _nextIcon = string.Empty;

    [ObservableProperty]
    private string _nextName = string.Empty;

    /// <summary>"Next payment · tomorrow".</summary>
    [ObservableProperty]
    private string _nextCaption = string.Empty;

    [ObservableProperty]
    private string _nextAmount = string.Empty;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasActive;

    [ObservableProperty]
    private bool _hasInactive;

    /// <summary>The empty state has its own button; otherwise the floating one adds a charge.</summary>
    [ObservableProperty]
    private bool _hasCharges;

    [ObservableProperty]
    private bool _hasChart;

    [ObservableProperty]
    private IReadOnlyList<ChartSlice> _chartSlices = [];

    [ObservableProperty]
    private IReadOnlyList<LegendItem> _legend = [];

    [ObservableProperty]
    private string _chartCenterValue = "0";

    [ObservableProperty]
    private string _chartCenterLabel = string.Empty;

    protected override async Task LoadCoreAsync()
    {
        var today = DateTime.Today;
        var overview = await store.GetSubscriptionOverviewAsync(today);
        var formatter = settings.Formatter;

        TodayText = Loc.Date(today, "dddd d MMMM");
        (MonthlyPrefix, MonthlyWhole, MonthlyFraction) = formatter.Split(overview.MonthlyTotal);
        YearlyText = Loc.Format("Subs_PerYear", formatter.Format(overview.YearlyTotal));
        PrivacyIcon = formatter.HideAmounts ? Icons.EyeOff : Icons.Eye;

        HasNext = overview.Next is not null;
        if (overview.Next is { } next)
        {
            NextIcon = next.Subscription.Icon;
            NextName = next.Subscription.Name;
            NextCaption = $"{Loc.Get("Subs_Next")} · {SubscriptionItemViewModel.DueText(next.NextPayment, today, capitalize: false)}";
            NextAmount = formatter.Format(next.Subscription.Amount);
        }

        Active.Clear();
        foreach (var subscription in overview.Active)
            Active.Add(new SubscriptionItemViewModel(subscription, today, formatter, OpenAsync));
        Inactive.Clear();
        foreach (var subscription in overview.Inactive)
            Inactive.Add(new SubscriptionItemViewModel(subscription, today, formatter, OpenAsync));

        HasActive = Active.Count > 0;
        HasInactive = Inactive.Count > 0;
        IsEmpty = !HasActive && !HasInactive;
        HasCharges = !IsEmpty;

        HasChart = overview.MonthlyTotal > 0;
        ChartCenterValue = Active.Count.ToString(Localizer.Instance.Culture);
        ChartCenterLabel = Loc.Noun(Active.Count, "Subscription");
        // By category as soon as there are several; with a single one, by charge is more telling.
        var byCategory = overview.ByCategory;
        (ChartSlices, Legend) = byCategory.Count > 1
            ? Breakdown.Build(
                byCategory.Select(c => (ChargeCategories.Name(c.Category), ChargeCategories.Color(c.Category), c.MonthlyCost)),
                formatter)
            : Breakdown.Build(
                overview.Active.Select(s => (s.Subscription.Name, Color.FromArgb(s.Subscription.ColorHex), s.MonthlyCost)),
                formatter);

        IsLoaded = true;
    }

    private Task OpenAsync(int subscriptionId) =>
        Shell.Current.GoToAsync($"{Routes.EditSubscription}?id={subscriptionId}");

    [RelayCommand]
    private Task AddSubscriptionAsync() => Shell.Current.GoToAsync(Routes.EditSubscription);

    [RelayCommand]
    private void TogglePrivacy()
    {
        settings.HideAmounts = !settings.HideAmounts;
        Palette.Haptic();
    }

    [RelayCommand]
    private Task OpenSettingsAsync() => Shell.Current.GoToAsync(Routes.Settings);

    [RelayCommand]
    private async Task LoadSamplesAsync()
    {
        await store.SeedSampleSubscriptionsAsync(
            DateTime.Today,
            new SampleSubscriptionTexts(
                Loc.Get("Sample_Gym"),
                Loc.Get("Sample_HomeInsurance"),
                Loc.Get("Sample_HealthInsurance"),
                Loc.Get("Sample_CarInsurance"),
                Loc.Get("Sample_CarLoan")));
        Palette.Haptic();
    }
}

public sealed class SubscriptionItemViewModel
{
    /// <summary>Payments this close are highlighted.</summary>
    private const int SoonDays = 3;

    public SubscriptionItemViewModel(SubscriptionSummary summary, DateTime today, MoneyFormatter formatter, Func<int, Task> open)
    {
        var subscription = summary.Subscription;
        Id = subscription.Id;
        Name = subscription.Name;
        Icon = subscription.Icon;
        Color = Color.FromArgb(subscription.ColorHex);
        SoftColor = Palette.Soft(Color);
        AmountText = formatter.Format(subscription.Amount);
        PeriodText = PerPeriod(subscription.Period);
        IsActive = subscription.IsActive;
        Subtitle = $"{(IsActive ? DueText(summary.NextPayment, today) : Loc.Get("Subs_Paused"))} · {ChargeCategories.Name(subscription.Category)}";
        IsDueSoon = IsActive && (summary.NextPayment - today.Date).Days <= SoonDays;
        OpenCommand = new AsyncRelayCommand(() => open(Id));
    }

    public int Id { get; }

    public string Name { get; }

    public string Icon { get; }

    public Color Color { get; }

    public Color SoftColor { get; }

    public string AmountText { get; }

    /// <summary>"/mois", "/an"…</summary>
    public string PeriodText { get; }

    public string Subtitle { get; }

    public bool IsActive { get; }

    public bool IsDueSoon { get; }

    public double RowOpacity => IsActive ? 1 : 0.55;

    public IAsyncRelayCommand OpenCommand { get; }

    /// <summary>"/mois", "/an"… appended to a price.</summary>
    public static string PerPeriod(BillingPeriod period) => period switch
    {
        BillingPeriod.Weekly => Loc.Get("Period_PerWeek"),
        BillingPeriod.Monthly => Loc.Get("Period_PerMonth"),
        BillingPeriod.Quarterly => Loc.Get("Period_PerQuarter"),
        BillingPeriod.Semiannual => Loc.Get("Period_PerSemester"),
        _ => Loc.Get("Period_PerYear"),
    };

    /// <summary>"Today", "Tomorrow", "In 3 days" within a week, then the date ("26 Oct").</summary>
    public static string DueText(DateTime payment, DateTime today, bool capitalize = true)
    {
        var days = (payment.Date - today.Date).Days;
        if (days > 7)
            return Loc.Date(payment, "d MMM");
        return capitalize ? Loc.Capitalize(Loc.When(days)) : Loc.When(days);
    }
}
