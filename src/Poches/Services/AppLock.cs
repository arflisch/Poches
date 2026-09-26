using Poches.Localization;

namespace Poches.Services;

/// <summary>
/// Hides the app behind a lock screen when it opens and whenever it comes back from the background, until the
/// owner authenticates. Covering the app as it leaves also keeps amounts out of the app switcher.
/// </summary>
public sealed class AppLock(AppSettings settings, DeviceAuthentication authentication)
{
    private Window? _window;
    private bool _promptPending;
    private bool _authenticating;

    /// <summary>Whether the lock screen is currently shown.</summary>
    public static bool IsLocked { get; private set; }

    /// <summary>Only when the owner turned it on and can still authenticate (a passcode is set).</summary>
    public bool IsEnabled => settings.LockEnabled && authentication.IsSupported && authentication.IsAvailable;

    public void Attach(Window window)
    {
        _window = window;
        window.Created += (_, _) => LockIfEnabled();
        window.Stopped += (_, _) => LockIfEnabled();
        // Ask right away when the app comes to the front; the Face ID sheet itself deactivates the app, so
        // only once per return (afterwards, the button on the lock screen asks again).
        window.Activated += (_, _) =>
        {
            if (IsLocked && _promptPending)
                _ = UnlockAsync();
        };
    }

    public async Task UnlockAsync()
    {
        _promptPending = false;
        if (_authenticating)
            return;

        _authenticating = true;
        try
        {
            if (await authentication.AuthenticateAsync(Loc.Get("Lock_Reason")))
            {
                IsLocked = false;
                LockScreen.Hide();
            }
        }
        finally
        {
            _authenticating = false;
        }
    }

    private void LockIfEnabled()
    {
        if (!IsEnabled || _window is null)
            return;

        _promptPending = true;
        if (IsLocked)
            return;

        IsLocked = true;
        LockScreen.Show(_window, authentication.Biometry, UnlockAsync);
    }
}
