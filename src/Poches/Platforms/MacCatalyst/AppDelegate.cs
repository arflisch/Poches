using Foundation;
using UIKit;

namespace Poches;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override UIKeyCommand[] KeyCommands => KeyboardShortcuts.Current;

	[Export("pochesKey:")]
	private void OnKey(UIKeyCommand command) => KeyboardShortcuts.Handle(command);

	[Export("pochesProbe:")]
	private void OnProbe(NSObject? sender)
	{
	}

	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		KeyboardShortcuts.Initialize();

		// Selected tab in the brand indigo, as on iPhone.
		UITabBar.Appearance.TintColor = new UIColor(traits =>
			traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark
				? UIColor.FromRGB(0x81, 0x8C, 0xF8)
				: UIColor.FromRGB(0x4F, 0x46, 0xE5));
		return base.FinishedLaunching(application, launchOptions);
	}
}
