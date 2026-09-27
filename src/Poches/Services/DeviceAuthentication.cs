#if IOS || MACCATALYST
using LocalAuthentication;
#elif ANDROID
using Android.Content.PM;
using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
#endif
using Poches.Localization;

namespace Poches.Services;

public enum BiometryKind
{
    None,
    FaceId,
    TouchId,
    OpticId,
    Fingerprint,
}

/// <summary>
/// Asks the device owner to prove who they are: Face ID or Touch ID, or fingerprint / face unlock on Android,
/// falling back to the device passcode (or the Mac password) so nobody gets locked out of their own data.
/// </summary>
public sealed class DeviceAuthentication
{
#if ANDROID
    // Face unlock on many Android phones is "weak" (class 2) biometrics; the PIN, pattern or password is the fallback.
    private const int Authenticators =
        BiometricManager.Authenticators.BiometricWeak | BiometricManager.Authenticators.DeviceCredential;
#endif

    /// <summary>
    /// Whether this platform can lock the app: iPhone, iPad, Mac, and Android 11+ (earlier versions cannot combine
    /// biometrics with the PIN fallback).
    /// </summary>
    public bool IsSupported =>
        OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsAndroidVersionAtLeast(30);

    /// <summary>Whether a passcode or biometrics is set up, i.e. whether authenticating can succeed.</summary>
    public bool IsAvailable
    {
        get
        {
#if IOS || MACCATALYST
            using var context = new LAContext();
            return context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out _);
#elif ANDROID
            return IsSupported
                && BiometricManager.From(Platform.AppContext).CanAuthenticate(Authenticators) == BiometricManager.BiometricSuccess;
#else
            return false;
#endif
        }
    }

    public BiometryKind Biometry
    {
        get
        {
#if IOS || MACCATALYST
            using var context = new LAContext();
            // The biometry type is only filled in once a policy has been evaluated.
            context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out _);
            return context.BiometryType switch
            {
                LABiometryType.FaceId => BiometryKind.FaceId,
                LABiometryType.TouchId => BiometryKind.TouchId,
                LABiometryType.OpticId => BiometryKind.OpticId,
                _ => BiometryKind.None,
            };
#elif ANDROID
            // Android does not say which biometrics will be offered; the fingerprint is by far the most common.
            return Platform.AppContext.PackageManager?.HasSystemFeature(PackageManager.FeatureFingerprint) == true
                ? BiometryKind.Fingerprint
                : BiometryKind.None;
#else
            return BiometryKind.None;
#endif
        }
    }

    /// <summary>"Face ID", "Touch ID"…, or null when only the passcode is available.</summary>
    public string? BiometryName => Biometry switch
    {
        BiometryKind.FaceId => "Face ID",
        BiometryKind.TouchId => "Touch ID",
        BiometryKind.OpticId => "Optic ID",
        BiometryKind.Fingerprint => Loc.Get("Lock_Fingerprint"),
        _ => null,
    };

    /// <summary>Returns true once the owner is recognised; false if they cancel or fail.</summary>
    public async Task<bool> AuthenticateAsync(string reason)
    {
#if IOS || MACCATALYST
        using var context = new LAContext { LocalizedCancelTitle = Loc.Get("Common_Cancel") };
        var (success, _) = await context.EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthentication, reason);
        return success;
#elif ANDROID
        if (Platform.CurrentActivity is not FragmentActivity activity)
            return false;
        var result = new TaskCompletionSource<bool>();
        var prompt = new BiometricPrompt(activity, ContextCompat.GetMainExecutor(activity)!, new PromptCallback(result));
        // No cancel button: with the PIN fallback, Android provides its own.
        prompt.Authenticate(new BiometricPrompt.PromptInfo.Builder()
            .SetTitle(reason)
            .SetAllowedAuthenticators(Authenticators)
            .SetConfirmationRequired(false)
            .Build());
        return await result.Task;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

#if ANDROID
    private sealed class PromptCallback(TaskCompletionSource<bool> result) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult authResult) =>
            result.TrySetResult(true);

        // Cancelled, too many attempts… A single unrecognised finger only calls OnAuthenticationFailed and the
        // prompt stays open for another try.
        public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString) =>
            result.TrySetResult(false);
    }
#endif
}
