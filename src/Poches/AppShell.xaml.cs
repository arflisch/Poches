using Poches.Services;
using Poches.Views;

namespace Poches;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(Routes.Pocket, typeof(PocketDetailPage));
        Routing.RegisterRoute(Routes.EditPocket, typeof(EditPocketPage));
        Routing.RegisterRoute(Routes.Movement, typeof(MovementPage));
        Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));
        Routing.RegisterRoute(Routes.EditSubscription, typeof(SubscriptionEditPage));

#if ANDROID
        // Android draws an opaque bottom bar: match the app's surfaces and accent (iOS keeps its native glass bar).
        this.SetAppThemeColor(TabBarBackgroundColorProperty, Color.FromArgb("#FFFFFF"), Color.FromArgb("#161922"));
        this.SetAppThemeColor(TabBarForegroundColorProperty, Color.FromArgb("#4F46E5"), Color.FromArgb("#818CF8"));
        this.SetAppThemeColor(TabBarTitleColorProperty, Color.FromArgb("#4F46E5"), Color.FromArgb("#818CF8"));
        this.SetAppThemeColor(TabBarUnselectedColorProperty, Color.FromArgb("#94A3B8"), Color.FromArgb("#5B6475"));
#endif
    }
}
