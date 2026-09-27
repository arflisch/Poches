using Poches.Localization;

namespace Poches.Services;

/// <summary>
/// Hides the app behind a lock screen when it opens and whenever it comes back from the background, until the
/// owner taps the unlock button and authenticates. Covering the app as it leaves also keeps amounts out of
/// the app switcher.
/// </summary>
public sealed class AppLock(AppSettings settings, DeviceAuthentication authentication)
{
    private Window? _window;
    private bool _authenticating;

    /// <summary>Whether the lock screen is currently shown.</summary>
    public static bool IsLocked { get; private set; }

    /// <summary>Only when the owner turned it on and can still authenticate (a passcode is set).</summary>
    public bool IsEnabled => settings.LockEnabled && authentication.IsSupported && authentication.IsAvailable;

    public void Attach(Window window)
    {
        _window = window;
        window.Created += (_, _) =>
        {
            LockScreen.HideFromRecents(IsEnabled);
            LockIfEnabled();
        };
        window.Stopped += (_, _) => LockIfEnabled();
        settings.Changed += (_, _) => LockScreen.HideFromRecents(IsEnabled);
    }

    /// <summary>Called by the lock screen's button: Face ID is only asked for when the owner wants it.</summary>
    public async Task UnlockAsync()
    {
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
        if (!IsEnabled || _window is null || IsLocked)
            return;

        IsLocked = true;
        LockScreen.Show(_window, authentication.Biometry, UnlockAsync);
    }
}
