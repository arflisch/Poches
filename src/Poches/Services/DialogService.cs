namespace Poches.Services;

public interface IDialogService
{
    Task AlertAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel = "Annuler");

    /// <summary>Shows an action sheet and returns the chosen option, or null when cancelled.</summary>
    Task<string?> ChooseAsync(string title, string? destructive, params string[] options);

    /// <summary>Asks for a short text; returns null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string placeholder, string initialValue, int maxLength);
}

public sealed class DialogService : IDialogService
{
    private const string Cancel = "Annuler";

    public async Task AlertAsync(string title, string message)
    {
        await WaitForPendingDismissalAsync();
        await CurrentPage.DisplayAlertAsync(title, message, "OK");
    }

    public async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel = Cancel)
    {
        await WaitForPendingDismissalAsync();
        return await CurrentPage.DisplayAlertAsync(title, message, accept, cancel);
    }

    public async Task<string?> ChooseAsync(string title, string? destructive, params string[] options)
    {
        await WaitForPendingDismissalAsync();
        var choice = await CurrentPage.DisplayActionSheetAsync(title, Cancel, destructive, options);
        return choice is null or Cancel ? null : choice;
    }

    public async Task<string?> PromptAsync(string title, string placeholder, string initialValue, int maxLength)
    {
        await WaitForPendingDismissalAsync();
        return await CurrentPage.DisplayPromptAsync(title, null, "OK", Cancel, placeholder, maxLength, Keyboard.Text, initialValue);
    }

    /// <summary>The top-most page, including modal sheets, so dialogs appear above them.</summary>
    private static Page CurrentPage
    {
        get
        {
            var root = Application.Current?.Windows.FirstOrDefault()?.Page
                ?? throw new InvalidOperationException("No window is open.");
            return root.Navigation.ModalStack.LastOrDefault() ?? Shell.Current?.CurrentPage ?? root;
        }
    }

    /// <summary>
    /// iOS silently drops an alert presented while another controller (file picker, share sheet)
    /// is still animating away, leaving the awaiting code stuck forever. Wait for it to settle first.
    /// </summary>
    private static async Task WaitForPendingDismissalAsync()
    {
#if IOS || MACCATALYST
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var top = Platform.GetCurrentUIViewController();
            // The document picker closes itself right after reporting the chosen file.
            var settling = top is not null
                && (top.IsBeingDismissed || top.IsBeingPresented || top is UIKit.UIDocumentPickerViewController);
            if (!settling)
                return;
            await Task.Delay(50);
        }
#else
        await Task.CompletedTask;
#endif
    }
}
