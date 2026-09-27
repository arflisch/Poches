#if IOS || MACCATALYST
using Microsoft.Maui.Platform;
using UIKit;
#elif ANDROID
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Platform;
using AColor = Android.Graphics.Color;
using Color = Microsoft.Maui.Graphics.Color;
using Window = Microsoft.Maui.Controls.Window;
#endif
using Poches.Localization;

namespace Poches.Services;

/// <summary>
/// The screen shown while the app is locked, above everything else (sheets, alerts, file pickers) so nothing
/// can show through or be tapped behind it: its own window on iPhone, the top-most view of the window on Mac,
/// a full-screen dialog on Android.
/// </summary>
public static class LockScreen
{
#if IOS || MACCATALYST
    private static UIWindow? _lockWindow;
    private static LockViewController? _lockController;

    public static void Show(Window window, BiometryKind biometry, Func<Task> unlock)
    {
        if (_lockController is not null)
            return;
        var appWindow = window.Handler?.PlatformView as UIWindow;
        var scene = appWindow?.WindowScene
            ?? UIApplication.SharedApplication.ConnectedScenes.ToArray().OfType<UIWindowScene>().FirstOrDefault();
        if (scene is null)
            return;

        _lockController = new LockViewController(biometry, unlock);
        if (OperatingSystem.IsMacCatalyst() && appWindow is not null)
        {
            // On a Mac, extra windows of a scene are not reliably drawn: cover the app window's content instead.
            var cover = _lockController.View!;
            cover.Frame = appWindow.Bounds;
            cover.AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
            appWindow.AddSubview(cover);
            return;
        }

        _lockWindow = new UIWindow(scene)
        {
            WindowLevel = UIWindowLevel.Alert + 1,
            RootViewController = _lockController,
        };
        _lockWindow.MakeKeyAndVisible();
    }

    public static void Hide()
    {
        if (_lockController is not { } controller)
            return;
        var lockWindow = _lockWindow;
        _lockController = null;
        _lockWindow = null;
        var view = lockWindow ?? controller.View!;
        UIView.Animate(0.25, () => view.Alpha = 0, () =>
        {
            if (lockWindow is null)
            {
                controller.View!.RemoveFromSuperview();
                return;
            }
            lockWindow.Hidden = true;
            lockWindow.RootViewController = null;
            // Give the keyboard focus back to the app.
            lockWindow.WindowScene?.Windows.FirstOrDefault(w => w != lockWindow && !w.Hidden)?.MakeKeyWindow();
        });
    }

    private sealed class LockViewController(BiometryKind biometry, Func<Task> unlock) : UIViewController
    {
        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            var resources = Application.Current!.Resources;
            View!.BackgroundColor = Dynamic((Color)resources["PageLight"], (Color)resources["PageDark"]);
            var accent = Dynamic(Color.FromArgb("#4F46E5"), Color.FromArgb("#818CF8"));

            var symbol = new UIImageView(UIImage.GetSystemImage(
                "lock.fill", UIImageSymbolConfiguration.Create(34, UIImageSymbolWeight.Semibold)))
            {
                TintColor = accent,
                ContentMode = UIViewContentMode.Center,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            var badge = new UIView
            {
                BackgroundColor = Dynamic(Color.FromArgb("#264F46E5"), Color.FromArgb("#33818CF8")),
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            badge.Layer.CornerRadius = 28;
            badge.AddSubview(symbol);

            var title = new UILabel
            {
                Text = Loc.Get("Lock_LockedTitle"),
                Font = Font("OpenSans-Semibold", 24, UIFontWeight.Semibold),
                TextColor = UIColor.Label,
                TextAlignment = UITextAlignment.Center,
            };
            var text = new UILabel
            {
                Text = Loc.Get("Lock_LockedText"),
                Font = Font("OpenSans-Regular", 16, UIFontWeight.Regular),
                TextColor = UIColor.SecondaryLabel,
                TextAlignment = UITextAlignment.Center,
                Lines = 0,
            };

            var configuration = UIButtonConfiguration.FilledButtonConfiguration;
            configuration.Title = Loc.Get("Lock_Unlock");
            configuration.BaseBackgroundColor = accent;
            configuration.CornerStyle = UIButtonConfigurationCornerStyle.Capsule;
            configuration.ContentInsets = new NSDirectionalEdgeInsets(14, 28, 14, 28);
            configuration.ImagePadding = 10;
            if (biometry switch { BiometryKind.FaceId => "faceid", BiometryKind.TouchId => "touchid", BiometryKind.OpticId => "opticid", _ => null } is { } icon)
                configuration.Image = UIImage.GetSystemImage(icon);
            var button = UIButton.GetButton(configuration, UIAction.Create(action => _ = unlock()));

            var stack = new UIStackView([badge, title, text, button])
            {
                Axis = UILayoutConstraintAxis.Vertical,
                Alignment = UIStackViewAlignment.Center,
                Spacing = 14,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            stack.SetCustomSpacing(24, badge);
            stack.SetCustomSpacing(28, text);
            View.AddSubview(stack);

            NSLayoutConstraint.ActivateConstraints(
            [
                badge.WidthAnchor.ConstraintEqualTo(84),
                badge.HeightAnchor.ConstraintEqualTo(84),
                symbol.CenterXAnchor.ConstraintEqualTo(badge.CenterXAnchor),
                symbol.CenterYAnchor.ConstraintEqualTo(badge.CenterYAnchor),
                stack.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor),
                stack.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor),
                stack.WidthAnchor.ConstraintLessThanOrEqualTo(View.WidthAnchor, 1, -64),
                stack.WidthAnchor.ConstraintLessThanOrEqualTo(420),
            ]);
        }

        private static UIColor Dynamic(Color light, Color dark)
        {
            var (l, d) = (light.ToPlatform(), dark.ToPlatform());
            return new UIColor(traits => traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark ? d : l);
        }

        private static UIFont Font(string name, float size, UIFontWeight fallbackWeight) =>
            UIFont.FromName(name, size) ?? UIFont.SystemFontOfSize(size, fallbackWeight)!;
    }
#elif ANDROID
    private static Android.App.Dialog? _dialog;

    public static void Show(Window window, BiometryKind biometry, Func<Task> unlock)
    {
        if (_dialog is not null || Platform.CurrentActivity is not { } activity)
            return;

        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var resources = Application.Current!.Resources;
        AColor Resource(string key) => ((Color)resources[key + (dark ? "Dark" : "Light")]).ToPlatform();
        var accent = Color.FromArgb(dark ? "#818CF8" : "#4F46E5").ToPlatform();
        var density = activity.Resources!.DisplayMetrics!.Density;
        int Dp(float value) => (int)(value * density + 0.5f);

        var root = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        root.SetGravity(GravityFlags.Center);
        root.SetBackgroundColor(Resource("Page"));
        root.SetPadding(Dp(32), 0, Dp(32), 0);

        var badge = new TextView(activity) { Text = "🔒", Gravity = GravityFlags.Center };
        badge.SetTextSize(Android.Util.ComplexUnitType.Sp, 34);
        badge.Background = Rounded(AColor.Argb(dark ? 0x33 : 0x26, accent.R, accent.G, accent.B), Dp(28));
        root.AddView(badge, new LinearLayout.LayoutParams(Dp(84), Dp(84)) { BottomMargin = Dp(24) });

        var title = Text(activity, Loc.Get("Lock_LockedTitle"), 24, Resource("TextPrimary"), "OpenSans-Semibold.ttf");
        root.AddView(title, Wrap(Dp(14)));
        var text = Text(activity, Loc.Get("Lock_LockedText"), 16, Resource("TextSecondary"), "OpenSans-Regular.ttf");
        root.AddView(text, Wrap(Dp(28)));

        var button = new Android.Widget.Button(activity) { Text = Loc.Get("Lock_Unlock"), Background = Rounded(accent, Dp(28)) };
        button.SetAllCaps(false);
        button.SetTextColor(AColor.White);
        button.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
        button.SetTypeface(Font(activity, "OpenSans-Semibold.ttf"), TypefaceStyle.Normal);
        button.SetPadding(Dp(32), Dp(14), Dp(32), Dp(14));
        button.StateListAnimator = null;
        button.Click += (_, _) => _ = unlock();
        root.AddView(button, Wrap(0));

        // Not cancellable: the back button must not reveal the app. It leaves it instead, as from any first screen.
        var dialog = new Android.App.Dialog(activity, Android.Resource.Style.ThemeDeviceDefaultNoActionBar);
        dialog.SetContentView(root);
        dialog.SetCancelable(false);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            dialog.OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(
                Android.Window.IOnBackInvokedDispatcher.PriorityOverlay, new LeaveOnBack(activity));
        }
        dialog.KeyPress += (_, e) =>
        {
            e.Handled = e.KeyCode == Keycode.Back;
            if (e.Handled && e.Event?.Action == KeyEventActions.Up)
                activity.MoveTaskToBack(true);
        };
        dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        dialog.Window?.SetBackgroundDrawable(new ColorDrawable(Resource("Page")));
        dialog.Show();
        _dialog = dialog;

        LinearLayout.LayoutParams Wrap(int bottomMargin) =>
            new(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = bottomMargin };
    }

    public static void Hide()
    {
        _dialog?.Dismiss();
        _dialog = null;
    }

    /// <summary>Keeps amounts out of the recent-apps list while the lock is on (Android 13+).</summary>
    public static void HideFromRecents(bool hide)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            Platform.CurrentActivity?.SetRecentsScreenshotEnabled(!hide);
    }

    private sealed class LeaveOnBack(Android.App.Activity activity) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked() => activity.MoveTaskToBack(true);
    }

    private static TextView Text(Android.Content.Context context, string value, float size, AColor color, string font)
    {
        var view = new TextView(context) { Text = value, Gravity = GravityFlags.Center };
        view.SetTextSize(Android.Util.ComplexUnitType.Sp, size);
        view.SetTextColor(color);
        view.SetTypeface(Font(context, font), TypefaceStyle.Normal);
        return view;
    }

    private static Typeface? Font(Android.Content.Context context, string file)
    {
        try
        {
            return Typeface.CreateFromAsset(context.Assets, file);
        }
        catch (Exception)
        {
            return Typeface.Default;
        }
    }

    private static GradientDrawable Rounded(AColor color, float radius)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(color);
        drawable.SetCornerRadius(radius);
        return drawable;
    }
#else
    public static void Show(Window window, BiometryKind biometry, Func<Task> unlock)
    {
    }

    public static void Hide()
    {
    }
#endif

#if !ANDROID
    /// <summary>On Apple platforms the lock screen itself covers the app switcher preview.</summary>
    public static void HideFromRecents(bool hide)
    {
    }
#endif
}
