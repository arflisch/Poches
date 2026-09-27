#if IOS || MACCATALYST
using UIKit;
using UniformTypeIdentifiers;
#endif

namespace Poches.Services;

/// <summary>Lets the user choose a backup file to restore and, on a Mac, where to save a new one.</summary>
public sealed class BackupFilePicker(IFilePicker filePicker)
{
    /// <summary>Returns the chosen file's content, or null if the user cancelled.</summary>
    public async Task<Stream?> OpenAsync()
    {
#if IOS || MACCATALYST
        // MAUI's picker loses the chosen file on iOS 26: its "sheet dismissed" handler reports a
        // cancellation before the asynchronous file access completes. Picking a copy and completing
        // synchronously in the pick callback avoids that race.
        _ = filePicker;
        var path = await PickJsonCopyAsync();
        return path is null ? null : File.OpenRead(path);
#else
        var file = await filePicker.PickAsync(new PickOptions { FileTypes = JsonFiles });
        return file is null ? null : await file.OpenReadAsync();
#endif
    }

#if IOS
    /// <summary>Shows the share sheet for the file; true once it was saved or sent, false if the sheet was closed.</summary>
    public Task<bool> ShareAsync(string path)
    {
        var result = new TaskCompletionSource<bool>();
        var sheet = new UIActivityViewController([Foundation.NSUrl.FromFilename(path)], null)
        {
            CompletionWithItemsHandler = (_, completed, _, _) => result.TrySetResult(completed),
        };

        var presenter = Platform.GetCurrentUIViewController()
            ?? throw new InvalidOperationException("No view controller to present the share sheet from.");
        if (sheet.PopoverPresentationController is { } popover && presenter.View is { } view)
        {
            // iPad: anchor the popover in the middle of the screen.
            popover.SourceView = view;
            popover.SourceRect = new CoreGraphics.CGRect(view.Bounds.Width / 2, view.Bounds.Height / 2, 0, 0);
            popover.PermittedArrowDirections = 0;
        }
        presenter.PresentViewController(sheet, true, null);
        return result.Task;
    }
#endif

#if MACCATALYST
    /// <summary>Shows the Mac "Save" panel for the file at <paramref name="path"/>; false if the user cancelled.</summary>
    public Task<bool> SaveAsync(string path)
    {
        var result = new TaskCompletionSource<bool>();
        var picker = new UIDocumentPickerViewController([Foundation.NSUrl.FromFilename(path)], asCopy: true);
        picker.DidPickDocumentAtUrls += (_, _) => result.TrySetResult(true);
        picker.WasCancelled += (_, _) => result.TrySetResult(false);

        var presenter = Platform.GetCurrentUIViewController()
            ?? throw new InvalidOperationException("No view controller to present the save panel from.");
        presenter.PresentViewController(picker, true, null);
        return result.Task;
    }
#endif

#if IOS || MACCATALYST
    private static Task<string?> PickJsonCopyAsync()
    {
        var result = new TaskCompletionSource<string?>();
        var picker = new UIDocumentPickerViewController([UTTypes.Json], asCopy: true) { AllowsMultipleSelection = false };
        picker.DidPickDocumentAtUrls += (_, e) => result.TrySetResult(e.Urls.FirstOrDefault()?.Path);
        picker.WasCancelled += (_, _) => result.TrySetResult(null);
        if (picker.PresentationController is { } presentation)
            presentation.Delegate = new SwipeDismissDelegate(() => result.TrySetResult(null));

        var presenter = Platform.GetCurrentUIViewController()
            ?? throw new InvalidOperationException("No view controller to present the file picker from.");
        presenter.PresentViewController(picker, true, null);
        return result.Task;
    }

    /// <summary>Treats a swipe-down on the picker sheet as a cancellation.</summary>
    private sealed class SwipeDismissDelegate(Action onDismissed) : UIAdaptivePresentationControllerDelegate
    {
        public override void DidDismiss(UIPresentationController presentationController) => onDismissed();
    }
#else
    private static readonly FilePickerFileType JsonFiles = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        // Some Android file managers report .json files as generic binaries or text.
        [DevicePlatform.Android] = ["application/json", "application/octet-stream", "text/plain"],
        [DevicePlatform.WinUI] = [".json"],
    });
#endif
}
