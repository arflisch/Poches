using System.Runtime.CompilerServices;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Poches.ViewModels;
using UIKit;

namespace Poches;

/// <summary>
/// Mac keyboard: Escape closes the current sheet, and the amount of a transaction can be typed instead of
/// clicked on the on-screen keypad (digits, comma or dot, backspace, Enter to save).
/// </summary>
/// <remarks>
/// The key commands live on the app delegate, at the end of the responder chain. A chain only exists while
/// something has the keyboard focus, which nothing has once a sheet opens, an alert closes or a text field is
/// left: each sheet therefore gets an invisible view that takes the focus back whenever it is lost.
/// </remarks>
internal static class KeyboardShortcuts
{
    public static readonly Selector Action = new("pochesKey:");

    /// <summary>Implemented by the app delegate: sending it succeeds only if something has the focus.</summary>
    public static readonly Selector Probe = new("pochesProbe:");

    private static readonly ConditionalWeakTable<Page, FocusHolder> Holders = new();

    private static readonly UIKeyCommand[] SheetKeys = [Command(UIKeyCommand.Escape)];

    private static readonly UIKeyCommand[] AmountKeys =
    [
        .. SheetKeys,
        .. "0123456789.,".Select(c => Command(c.ToString())),
        Command(UIKeyCommand.Delete),
        Command("\b"),
        Command("\r"),
    ];

    /// <summary>The keys to listen to right now: none while an alert or a file panel is in front.</summary>
    public static UIKeyCommand[] Current => TopSheet() switch
    {
        MovementViewModel => AmountKeys,
        ISheetViewModel => SheetKeys,
        _ => [],
    };

    public static void Initialize() => NSTimer.CreateRepeatingScheduledTimer(0.5, _ => KeepFocus());

    /// <summary>Makes <paramref name="page"/> (a sheet) receive the keyboard once it is on screen.</summary>
    public static void Attach(Page page)
    {
        page.Loaded += (_, _) =>
        {
            if (page.Handler?.PlatformView is not UIView root)
                return;
            var holder = Holders.GetValue(page, _ => new FocusHolder());
            if (holder.Superview != root)
                root.AddSubview(holder);
            holder.BecomeFirstResponder();
        };
    }

    /// <summary>Gives the focus back to the sheet in front when nothing has it (a text field keeps it).</summary>
    private static void KeepFocus()
    {
        if (TopSheet() is null || UIApplication.SharedApplication.SendAction(Probe, null, null, null))
            return;
        if (TopSheetPage() is { } page && Holders.TryGetValue(page, out var holder) && holder.Window is not null)
            holder.BecomeFirstResponder();
    }

    public static void Handle(UIKeyCommand command)
    {
        var sheet = TopSheet();
        if (command.Input == UIKeyCommand.Escape)
        {
            sheet?.DismissCommand.Execute(null);
            return;
        }
        if (sheet is not MovementViewModel movement)
            return;

        switch (command.Input)
        {
            case "\r":
                movement.SaveCommand.Execute(null);
                break;
            case "\b":
            case var input when input == UIKeyCommand.Delete:
                movement.KeyCommand.Execute("back");
                break;
            case "." or ",":
                movement.KeyCommand.Execute(",");
                break;
            case { } digit:
                movement.KeyCommand.Execute(digit);
                break;
        }
    }

    private static UIKeyCommand Command(string input)
    {
        var command = UIKeyCommand.Create((NSString)input, 0, Action);
        command.WantsPriorityOverSystemBehavior = true;
        return command;
    }

    private static Page? TopSheetPage() =>
        Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation.ModalStack.LastOrDefault();

    /// <summary>The sheet in front, unless something else (an alert, a file panel) is presented over it.</summary>
    private static ISheetViewModel? TopSheet() =>
        Platform.GetCurrentUIViewController() is UIAlertController or UIDocumentPickerViewController
            ? null
            : TopSheetPage()?.BindingContext as ISheetViewModel;

    /// <summary>Invisible view that can hold the keyboard focus.</summary>
    private sealed class FocusHolder() : UIView(CGRect.Empty)
    {
        public override bool CanBecomeFirstResponder => true;
    }
}
