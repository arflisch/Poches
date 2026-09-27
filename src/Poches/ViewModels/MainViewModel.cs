using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poches.Controls;
using Poches.Core.Data;
using Poches.Core.Formatting;
using Poches.Core.Models;
using Poches.Localization;
using Poches.Services;

namespace Poches.ViewModels;

public sealed partial class MainViewModel : ReloadingViewModel, IBreakdown
{
    private readonly BudgetStore _store;
    private readonly AppSettings _settings;
    private readonly BackupService _backup;
    private readonly IDialogService _dialogs;

    public MainViewModel(BudgetStore store, AppSettings settings, BackupService backup, IDialogService dialogs)
    {
        _store = store;
        _settings = settings;
        _backup = backup;
        _dialogs = dialogs;
    }

    public ObservableCollection<PocketItemViewModel> Pockets { get; } = [];

    [ObservableProperty]
    private string _todayText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ChartSlice> _chartSlices = [];

    [ObservableProperty]
    private IReadOnlyList<LegendItem> _legend = [];

    [ObservableProperty]
    private string _totalPrefix = string.Empty;

    [ObservableProperty]
    private string _totalWhole = "0";

    [ObservableProperty]
    private string _totalFraction = string.Empty;

    [ObservableProperty]
    private string _monthDeltaText = string.Empty;

    [ObservableProperty]
    private bool _hasMonthDelta;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasPockets;

    [ObservableProperty]
    private bool _hasChart;

    [ObservableProperty]
    private string _chartCenterValue = "0";

    [ObservableProperty]
    private string _chartCenterLabel = string.Empty;

    [ObservableProperty]
    private string _privacyIcon = Icons.Eye;

    [ObservableProperty]
    private bool _showBackupReminder;

    [ObservableProperty]
    private string _backupReminderText = string.Empty;

    protected override async Task LoadCoreAsync()
    {
        var overview = await _store.GetOverviewAsync(DateTime.Now);
        var formatter = _settings.Formatter;

        TodayText = Loc.Date(DateTime.Now, "dddd d MMMM");
        (TotalPrefix, TotalWhole, TotalFraction) = formatter.Split(overview.Total);
        HasMonthDelta = overview.MonthDeltaCents != 0 && !formatter.HideAmounts;
        MonthDeltaText = Loc.Format("Main_ThisMonth", formatter.FormatSigned(overview.MonthDelta));
        PrivacyIcon = formatter.HideAmounts ? Icons.EyeOff : Icons.Eye;

        Pockets.Clear();
        foreach (var pocket in overview.Pockets)
            Pockets.Add(new PocketItemViewModel(pocket, overview.ShareOf(pocket), formatter, OpenPocketAsync));

        HasPockets = Pockets.Count > 0;
        IsEmpty = !HasPockets;
        ChartCenterValue = Pockets.Count.ToString(Localizer.Instance.Culture);
        ChartCenterLabel = Loc.Noun(Pockets.Count, "Pocket");
        HasChart = Pockets.Any(p => p.Balance > 0);
        (ChartSlices, Legend) = Breakdown.Build(Pockets.Select(p => (p.Name, p.Color, p.Balance)), formatter);
        UpdateBackupReminder();
        IsLoaded = true;
    }

    private void UpdateBackupReminder()
    {
        var lastBackup = _settings.LastBackupAt;
        ShowBackupReminder = HasPockets && (lastBackup is null || DateTime.Now - lastBackup > BackupService.ReminderAge);
        BackupReminderText = lastBackup is { } date
            ? Loc.Format("Main_BackupLast", Loc.RelativeDate(date))
            : Loc.Get("Main_BackupNever");
    }

    private Task OpenPocketAsync(int pocketId) =>
        Shell.Current.GoToAsync($"{Routes.Pocket}?id={pocketId}");

    [RelayCommand]
    private Task AddPocketAsync() => Shell.Current.GoToAsync(Routes.EditPocket);

    [RelayCommand]
    private Task AddMovementAsync() =>
        Shell.Current.GoToAsync(HasPockets ? Routes.Movement : Routes.EditPocket);

    [RelayCommand]
    private void TogglePrivacy()
    {
        _settings.HideAmounts = !_settings.HideAmounts;
        Palette.Haptic();
    }

    [RelayCommand]
    private Task OpenSettingsAsync() => Shell.Current.GoToAsync(Routes.Settings);

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        try
        {
            if (await _backup.ShareBackupAsync() == BackupOutcome.Saved)
                SuccessToast.Show(Loc.Get("Backup_Saved"));
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Settings_BackupFailed"), Loc.Format("Settings_BackupFailedText", ex.Message));
        }
    }

    [RelayCommand]
    private async Task LoadSamplesAsync()
    {
        await _store.SeedSampleDataAsync(DateTime.Now, Loc.SampleTexts);
        Palette.Haptic();
    }
}

public sealed class PocketItemViewModel
{
    public PocketItemViewModel(PocketSummary summary, double share, MoneyFormatter formatter, Func<int, Task> open)
    {
        var pocket = summary.Pocket;
        Id = pocket.Id;
        Name = pocket.Name;
        Icon = pocket.Icon;
        Color = Color.FromArgb(pocket.ColorHex);
        SoftColor = Palette.Soft(Color);
        Balance = summary.Balance;
        BalanceText = formatter.Format(summary.Balance);
        ShareText = formatter.FormatPercent(share);
        HasGoal = summary.GoalProgress is not null;
        GoalProgress = summary.GoalProgress ?? 0;
        Caption = pocket.Goal is { } goal
            ? Loc.Format("Main_GoalProgress", formatter.FormatPercent(GoalProgress), formatter.FormatShort(goal))
            : Loc.Format("Main_ShareOfTotal", ShareText);
        OpenCommand = new AsyncRelayCommand(() => open(Id));
    }

    public int Id { get; }

    public string Name { get; }

    public string Icon { get; }

    public Color Color { get; }

    public Color SoftColor { get; }

    public decimal Balance { get; }

    public string BalanceText { get; }

    public string ShareText { get; }

    public bool HasGoal { get; }

    public double GoalProgress { get; }

    public string Caption { get; }

    public IAsyncRelayCommand OpenCommand { get; }
}
