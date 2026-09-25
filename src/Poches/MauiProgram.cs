using Microsoft.Extensions.Logging;
using Poches.Core.Data;
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
        builder.Services.AddSingleton<BackupFilePicker>();
        builder.Services.AddSingleton<BackupService>();
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
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsPage>();

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
