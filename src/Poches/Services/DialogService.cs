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

    public Task AlertAsync(string title, string message) =>
        CurrentPage.DisplayAlertAsync(title, message, "OK");

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel = Cancel) =>
        CurrentPage.DisplayAlertAsync(title, message, accept, cancel);

    public async Task<string?> ChooseAsync(string title, string? destructive, params string[] options)
    {
        var choice = await CurrentPage.DisplayActionSheetAsync(title, Cancel, destructive, options);
        return choice is null or Cancel ? null : choice;
    }

    public Task<string?> PromptAsync(string title, string placeholder, string initialValue, int maxLength) =>
        CurrentPage.DisplayPromptAsync(title, null, "OK", Cancel, placeholder, maxLength, Keyboard.Text, initialValue);

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
}
