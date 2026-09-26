#if IOS || MACCATALYST
using Microsoft.Maui.Platform;
using UIKit;
#endif
using Poches.Localization;

namespace Poches.Services;

/// <summary>
/// The screen shown while the app is locked, above everything else (sheets, alerts, file pickers) so nothing
/// can show through or be tapped behind it: its own window on iPhone, the top-most view of the window on Mac.
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
#else
    public static void Show(Window window, BiometryKind biometry, Func<Task> unlock)
    {
    }

    public static void Hide()
    {
    }
#endif
}
