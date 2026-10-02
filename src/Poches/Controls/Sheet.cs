using Microsoft.Maui.Controls.Shapes;
using iOSPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;
using PresentationStyle = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle;

namespace Poches.Controls;

/// <summary>
/// Adjusts the pages shown as sheets (<c>ios:Page.ModalPresentationStyle="FormSheet"</c>: a full-width sheet on iPhone,
/// a centred window on iPad) to the platform.
/// </summary>
public static class Sheet
{
    /// <summary>Call from the page constructor, after <c>InitializeComponent</c>.</summary>
    public static void Adapt(ContentPage page)
    {
#if IOS
        if (DeviceInfo.Idiom == DeviceIdiom.Tablet)
            page.Loaded += (_, _) => EnlargeOnTablet(page);
#endif
        if (!OperatingSystem.IsMacCatalyst())
            return;

        // On a Mac, MAUI never completes the navigation to a PageSheet or FormSheet: the sheet shows up, but
        // every later navigation waits for it, so it can't even be closed. Present the page over the whole
        // window instead and draw the sheet ourselves: a centred card on a dimmed background.
        page.SetValue(iOSPage.ModalPresentationStyleProperty, PresentationStyle.OverFullScreen);

        var resources = Application.Current!.Resources;
        var card = new Border
        {
            Content = page.Content,
            Margin = new Thickness(24),
            MaximumWidthRequest = 600,
            MaximumHeightRequest = 800,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
        };
        card.SetAppThemeColor(VisualElement.BackgroundColorProperty, (Color)resources["PageLight"], (Color)resources["PageDark"]);
        // In dark mode the card would melt into the dimmed window behind it.
        card.SetAppTheme<Brush>(Border.StrokeProperty, Brush.Transparent, new SolidColorBrush(Color.FromRgba(255, 255, 255, 0.14)));

        page.BackgroundColor = Color.FromRgba(0, 0, 0, 0.45);
        page.Content = card;
#if MACCATALYST
        KeyboardShortcuts.Attach(page);
#endif
    }

#if IOS
    /// <summary>
    /// On an iPad, a form sheet opens as a small centred window that cuts its content: give it the size of a phone
    /// screen instead, within the room available.
    /// </summary>
    private static void EnlargeOnTablet(ContentPage page)
    {
        if (page.Handler is not IPlatformViewHandler { ViewController: { } controller })
            return;

        // The presented controller is the one wrapping the page.
        var presented = controller;
        while (presented.ParentViewController is { } parent)
            presented = parent;

        var screen = UIKit.UIScreen.MainScreen.Bounds.Size;
        var height = Math.Min(900, Math.Max(screen.Height, screen.Width) - 160);
        presented.PreferredContentSize = new CoreGraphics.CGSize(Math.Min(640, screen.Width - 80), height);
    }
#endif
}
