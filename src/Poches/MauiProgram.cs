using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;
using Poches.Core.Data;
using Poches.Core.Investments;
using Poches.Services;
using Poches.ViewModels;
using Poches.Views;

namespace Poches;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseLocalNotification()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        ConfigureBorderlessInputs();

        builder.Services.AddSingleton(_ => new BudgetStore(Path.Combine(FileSystem.AppDataDirectory, "poches.db3")));
        builder.Services.AddSingleton(Preferences.Default);
        builder.Services.AddSingleton(Share.Default);
        builder.Services.AddSingleton(FilePicker.Default);
        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<DeviceAuthentication>();
        builder.Services.AddSingleton<AppLock>();
        builder.Services.AddSingleton<BackupFilePicker>();
        builder.Services.AddSingleton<PasswordPrompt>();
        builder.Services.AddSingleton<BackupService>();
        builder.Services.AddSingleton<SubscriptionReminders>();
        builder.Services.AddSingleton<ProService>();
        builder.Services.AddSingleton<ScheduledDeposits>();
        // Investments (Poches Pro): quotes from Yahoo Finance, only once the user turns online quotes on.
        builder.Services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 5 * 1024 * 1024 };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1");
            return new YahooFinanceClient(http);
        });
        builder.Services.AddSingleton<IQuoteSource>(sp => sp.GetRequiredService<YahooFinanceClient>());
        builder.Services.AddSingleton(sp => new MarketData(sp.GetRequiredService<BudgetStore>(), sp.GetRequiredService<IQuoteSource>()));
        builder.Services.AddSingleton<InvestmentService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<AppShell>();

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<PocketDetailViewModel>();
        builder.Services.AddTransient<PocketDetailPage>();
        builder.Services.AddTransient<EditPocketViewModel>();
        builder.Services.AddTransient<EditPocketPage>();
        builder.Services.AddTransient<MovementViewModel>();
        builder.Services.AddTransient<MovementPage>();
        builder.Services.AddSingleton<SubscriptionsViewModel>();
        builder.Services.AddSingleton<SubscriptionsPage>();
        builder.Services.AddTransient<SubscriptionEditViewModel>();
        builder.Services.AddTransient<SubscriptionEditPage>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ProViewModel>();
        builder.Services.AddTransient<ProPage>();
        builder.Services.AddTransient<ScheduledDepositViewModel>();
        builder.Services.AddTransient<ScheduledDepositPage>();
        builder.Services.AddTransient<SplitViewModel>();
        builder.Services.AddTransient<SplitPage>();
        builder.Services.AddTransient<StatisticsViewModel>();
        builder.Services.AddTransient<StatisticsPage>();
        builder.Services.AddSingleton<InvestmentsViewModel>();
        builder.Services.AddSingleton<InvestmentsPage>();
        builder.Services.AddTransient<HoldingDetailViewModel>();
        builder.Services.AddTransient<HoldingDetailPage>();
        builder.Services.AddTransient<SymbolSearchViewModel>();
        builder.Services.AddTransient<PurchaseEditViewModel>();
        builder.Services.AddTransient<PurchaseEditPage>();
        builder.Services.AddTransient<HoldingEditViewModel>();
        builder.Services.AddTransient<HoldingEditPage>();
        builder.Services.AddTransient<CurrencyFieldViewModel>();
        builder.Services.AddTransient<OperationEditViewModel>();
        builder.Services.AddTransient<OperationEditPage>();
        builder.Services.AddTransient<DetectedDividendsViewModel>();
        builder.Services.AddTransient<DetectedDividendsPage>();
        builder.Services.AddTransient<InvestmentPlanViewModel>();
        builder.Services.AddTransient<InvestmentPlanPage>();
        builder.Services.AddTransient<ContributionViewModel>();
        builder.Services.AddTransient<ContributionPage>();
        builder.Services.AddTransient<CsvImportViewModel>();
        builder.Services.AddTransient<CsvImportPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>Inputs are drawn inside our own rounded fields, so remove the native underline/border.</summary>
    private static void ConfigureBorderlessInputs()
    {
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("Borderless", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif IOS || MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });

        Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping("Borderless", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif IOS && !MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });
    }
}
