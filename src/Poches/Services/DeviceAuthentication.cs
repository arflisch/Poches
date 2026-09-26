#if IOS || MACCATALYST
using LocalAuthentication;
#endif
using Poches.Localization;

namespace Poches.Services;

public enum BiometryKind
{
    None,
    FaceId,
    TouchId,
    OpticId,
}

/// <summary>
/// Asks the device owner to prove who they are: Face ID or Touch ID, falling back to the device passcode (or the
/// Mac password) so nobody gets locked out of their own data.
/// </summary>
public sealed class DeviceAuthentication
{
    /// <summary>Whether this platform can lock the app (iPhone, iPad and Mac for now).</summary>
    public bool IsSupported => OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst();

    /// <summary>Whether a passcode or biometrics is set up, i.e. whether authenticating can succeed.</summary>
    public bool IsAvailable
    {
        get
        {
#if IOS || MACCATALYST
            using var context = new LAContext();
            return context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out _);
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
        _ => null,
    };

    /// <summary>Returns true once the owner is recognised; false if they cancel or fail.</summary>
    public async Task<bool> AuthenticateAsync(string reason)
    {
#if IOS || MACCATALYST
        using var context = new LAContext { LocalizedCancelTitle = Loc.Get("Common_Cancel") };
        var (success, _) = await context.EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthentication, reason);
        return success;
#else
        await Task.CompletedTask;
        return false;
#endif
    }
}
