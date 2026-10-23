namespace Codex.Domain.Web;

/// <summary>
/// What the restricted browser answers when a page asks for a device permission. Only the camera
/// is ever granted to a page: the browser holds no microphone permission, and a page asking for
/// anything else is refused.
///
/// <para>The camera is granted only from an origin the browser would itself follow, so an
/// embedded third-party frame cannot open it by being present. When the app does not yet hold the
/// camera, the decision is to ask the owner once and say nothing is granted until they answer.</para>
/// </summary>
public static class WebPermissionPlan
{
    // The WebKit resource names. Written out here so this file stays free of Android types; the
    // Android head passes PermissionRequest.GetResources() straight through.
    public const string VideoCapture = "android.webkit.resource.VIDEO_CAPTURE";
    public const string AudioCapture = "android.webkit.resource.AUDIO_CAPTURE";

    public static WebPermissionVerdict Decide(IReadOnlyCollection<string> requested, bool cameraGranted, bool originAllowed)
    {
        if (!originAllowed || !requested.Contains(VideoCapture))
        {
            return new WebPermissionVerdict([], NeedsCameraPrompt: false);
        }

        return cameraGranted
            ? new WebPermissionVerdict([VideoCapture], NeedsCameraPrompt: false)
            : new WebPermissionVerdict([], NeedsCameraPrompt: true);
    }
}

/// <summary>
/// The answer to one permission request: the resources to grant now, and whether the owner must
/// first be asked for the camera before the request can be granted or refused.
/// </summary>
public sealed record WebPermissionVerdict(IReadOnlyList<string> Grant, bool NeedsCameraPrompt);
