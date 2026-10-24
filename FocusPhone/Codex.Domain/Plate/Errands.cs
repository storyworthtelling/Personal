namespace Codex.Domain.Plate;

/// <summary>What another app has asked Plate to do.</summary>
public enum Errand
{
    /// <summary>Nothing was asked. Plate opens as itself.</summary>
    None,

    /// <summary>Take a picture and hand it back.</summary>
    Photo,

    /// <summary>Take a video and hand it back.</summary>
    Video,

    /// <summary>Show a camera. Nobody is waiting on a result.</summary>
    OpenCamera,

    /// <summary>Display an image somebody else owns.</summary>
    ShowImage,

    /// <summary>Read a business card and return name, phone and email as text.</summary>
    ReadCard,

    /// <summary>Read a statement and return a balance on the existing Index contract.</summary>
    ReadStatement,

    /// <summary>Read a recipe card across 1-N pages and return its title, ingredient lines and step lines as text.</summary>
    ReadRecipe,
}

/// <summary>
/// The camera component table as a function.
///
/// <para>Plate declares the full camera component set so nothing on this phone opens a
/// different camera, and that is exactly the thing that goes wrong quietly: <b>Android
/// silently omits an app missing a component</b>, with no warning. So the mapping is
/// written down where a test can read it, rather than only in attributes on an
/// Activity.</para>
/// </summary>
public static class Errands
{
    public const string ImageCapture = "android.media.action.IMAGE_CAPTURE";
    public const string ImageCaptureSecure = "android.media.action.IMAGE_CAPTURE_SECURE";
    public const string VideoCapture = "android.media.action.VIDEO_CAPTURE";
    public const string StillImageCamera = "android.media.action.STILL_IMAGE_CAMERA";
    public const string StillImageCameraSecure = "android.media.action.STILL_IMAGE_CAMERA_SECURE";
    public const string View = "android.intent.action.VIEW";

    public static Errand For(string? action, string? type) => action switch
    {
        ImageCapture or ImageCaptureSecure => Errand.Photo,
        VideoCapture => Errand.Video,
        StillImageCamera or StillImageCameraSecure => Errand.OpenCamera,
        // Plate shows pictures. A PDF handed to it by mistake should fall through to
        // whatever actually opens PDFs rather than being displayed as a broken image.
        View when type is not null && type.StartsWith("image/", StringComparison.Ordinal)
            => Errand.ShowImage,
        ReadRequest.ReadCard => Errand.ReadCard,
        ReadRequest.ReadStatement => Errand.ReadStatement,
        ReadRequest.ReadRecipe => Errand.ReadRecipe,
        _ => Errand.None,
    };

    /// <summary>
    /// Whether somebody is sitting in <c>onActivityResult</c> waiting. That is why an errand
    /// screen drops the binder gutter entirely — there is nowhere else to go, and the frame
    /// says so rather than a rule politely asking the owner not to wander.
    /// </summary>
    public static bool HasAWaitingCaller(Errand errand) =>
        errand is not (Errand.None or Errand.OpenCamera);
}
