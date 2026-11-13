namespace Codex.Domain.Plate;

/// <summary>Where the camera permission stands for the shutter.</summary>
public enum CameraAccess
{
    Allowed,

    /// <summary>Not granted, and the phone will still ask.</summary>
    NotAllowed,

    /// <summary>Not granted, and the last request came back refused without asking: only the phone's settings can turn it on.</summary>
    Refused,
}

/// <summary>
/// What the shutter says when it cannot take a picture (audit 11 §23). "The camera is not ready yet." covered a refused
/// permission too, which no amount of waiting fixes, and the permission was asked for only once, at launch.
/// </summary>
public static class CameraWords
{
    /// <param name="granted">Whether the camera permission is held now.</param>
    /// <param name="refusedWithoutAsking">Whether the last request came back denied with no dialog shown — Android's
    /// answer once the owner has refused for good.</param>
    public static CameraAccess Access(bool granted, bool refusedWithoutAsking) =>
        granted ? CameraAccess.Allowed : refusedWithoutAsking ? CameraAccess.Refused : CameraAccess.NotAllowed;

    /// <summary>Whether the shutter should ask for the permission again.</summary>
    public static bool ShouldAsk(CameraAccess access) => access == CameraAccess.NotAllowed;

    public static string ForShutter(CameraAccess access) => access switch
    {
        CameraAccess.NotAllowed => "Plate needs the camera to take a picture. Allow it when the phone asks.",
        CameraAccess.Refused => "Camera access is off for Plate. Turn it on in the phone's settings, under Apps, Plate, Permissions.",
        _ => "The camera is still starting. Try again in a moment.",
    };
}
