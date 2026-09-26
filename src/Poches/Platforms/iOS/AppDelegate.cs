using Foundation;
using UIKit;

namespace Poches;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		// Selected tab in the brand indigo. Only the tint is set, so the iOS 26 glass tab bar keeps its look.
		UITabBar.Appearance.TintColor = new UIColor(traits =>
			traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark
				? UIColor.FromRGB(0x81, 0x8C, 0xF8)
				: UIColor.FromRGB(0x4F, 0x46, 0xE5));
		return base.FinishedLaunching(application, launchOptions);
	}
}
