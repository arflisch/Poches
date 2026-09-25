#if IOS || MACCATALYST
using UIKit;
using UniformTypeIdentifiers;
#endif

namespace Poches.Services;

/// <summary>Lets the user choose a backup file and opens it for reading.</summary>
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
        var file = await filePicker.PickAsync(new PickOptions { PickerTitle = "Choisis une sauvegarde Poches", FileTypes = JsonFiles });
        return file is null ? null : await file.OpenReadAsync();
#endif
    }

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
