using Foundation;

namespace Poches;

/// <summary>
/// Apps built with the macOS 27 SDK stop at launch unless they adopt the scene lifecycle (as on iPhone): MAUI's
/// scene delegate creates the window (declared under UIApplicationSceneManifest in Info.plist).
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}
