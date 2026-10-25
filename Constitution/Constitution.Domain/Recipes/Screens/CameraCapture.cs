namespace Constitution.Domain.Recipes.Screens;

/// <summary>
/// Where the partner's one photograph lives and when it goes (C-6.5.2, D105). The photograph is a file the camera writes into a
/// folder under the app's cache directory, read on the phone by text recognition and deleted straight after, whatever the reading
/// came to. It is never in public storage and never in the media library, so no other app and no backup ever holds it. Pure: it
/// decides paths and the order of read-then-delete; the head owns the camera and the disk.
/// </summary>
public static class CameraCaptureCache
{
    /// <summary>The folder under the cache directory. The same name is the only path the capture provider serves.</summary>
    public const string Folder = "recipe-capture";

    /// <summary>
    /// The one file name. There is only ever one photograph at a time, so the answer to the camera can find it again even if the
    /// phone ended the app while the camera was open.
    /// </summary>
    public const string PageFileName = "page.jpg";

    /// <summary>The folder inside a cache directory.</summary>
    public static string FolderIn(string cacheRoot) => Path.Combine(cacheRoot, Folder);

    /// <summary>Where the photograph of a page is written.</summary>
    public static string PathIn(string cacheRoot) => Path.Combine(FolderIn(cacheRoot), PageFileName);

    /// <summary>True only for a file directly inside the capture folder, spelled without any way back out of it.</summary>
    public static bool IsCaptureFile(string cacheRoot, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
        ArgumentNullException.ThrowIfNull(path);

        if (path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var folder = FolderIn(cacheRoot).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        var parent = Path.GetDirectoryName(path);
        return parent is not null
            && Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar == folder
            && Path.GetFileName(path).Length > 0;
    }

    /// <summary>
    /// Reads one photograph and lets it go. <paramref name="recognize"/> turns the file into lines; <paramref name="delete"/> is
    /// called in a <c>finally</c>, so the photograph goes after a good read, after a read that threw and after a read that failed
    /// late, and the failure still reaches the caller with its stack. A file outside the capture folder is refused before anything
    /// is read and is never deleted: it is not this app's to remove.
    /// </summary>
    /// <param name="cacheRoot">The app's cache directory.</param>
    /// <param name="photoPath">The photograph the camera wrote.</param>
    /// <param name="recognize">The recognition, on the phone.</param>
    /// <param name="delete">Removes a file; true when it is gone.</param>
    /// <param name="forgotten">Told whether the delete worked, so a photograph that stayed is on the trail rather than quiet.</param>
    public static async Task<IReadOnlyList<string>> ReadAndForgetAsync(
        string cacheRoot,
        string photoPath,
        Func<string, Task<IReadOnlyList<string>>> recognize,
        Func<string, bool> delete,
        Action<bool>? forgotten = null)
    {
        ArgumentNullException.ThrowIfNull(recognize);
        ArgumentNullException.ThrowIfNull(delete);

        if (!IsCaptureFile(cacheRoot, photoPath))
        {
            throw new ArgumentException("The photograph is not in the capture folder, so it was neither read nor deleted.", nameof(photoPath));
        }

        try
        {
            return await recognize(photoPath).ConfigureAwait(false);
        }
        finally
        {
            var gone = delete(photoPath);
            forgotten?.Invoke(gone);
        }
    }
}

/// <summary>What to do next when the partner taps the camera row.</summary>
public enum CameraStep
{
    /// <summary>This phone has no camera at all.</summary>
    NoCamera,

    /// <summary>The camera permission has not been given: say why, then ask.</summary>
    AskFirst,

    /// <summary>Open the camera.</summary>
    Capture,
}

/// <summary>The camera permission step and its sentences, written once (D105). Typing is always still there after any of them.</summary>
public static class CameraAccess
{
    public const string AskingSentence =
        "Asking to use the camera, only to read a recipe card page on this phone. The photo is deleted straight after.";

    public const string DeniedSentence =
        "The camera was not allowed, so a card can't be photographed. You can still use " + RecipePickerText.TypeInTitle + ".";

    public const string NoCameraSentence =
        "This phone has no camera, so a card can't be photographed. You can still use " + RecipePickerText.TypeInTitle + ".";

    public const string CaptureFailedSentence = "The camera did not give back a photo, so the page was not added.";

    public const string ReadFailedSentence = "That page could not be read on this phone, so it was not added.";

    /// <summary>The step for a phone that has or lacks a camera and has or lacks the permission.</summary>
    public static CameraStep Decide(bool hasCamera, bool granted)
    {
        if (!hasCamera)
        {
            return CameraStep.NoCamera;
        }

        return granted ? CameraStep.Capture : CameraStep.AskFirst;
    }
}

/// <summary>What the camera trail line says happened. Never a word of the card and never a path.</summary>
public enum CameraEvent
{
    PageRead,
    NothingRead,
    ReadFailed,
    PhotoDeleted,
    PhotoKept,
    PermissionDenied,
}

/// <summary>
/// The camera's trail phrases. Each is made of the trail vocabulary and names the step, so the Diagnostics export can carry it; a
/// failed read names the exception's type and never its message, which could hold a recognised word.
/// </summary>
public static class RecipeCameraTrail
{
    public static string LogPhrase(CameraEvent step, Exception? failure)
    {
        switch (step)
        {
            case CameraEvent.PageRead:
                return "recipe camera: page read";
            case CameraEvent.NothingRead:
                return "recipe camera: nothing read";
            case CameraEvent.PhotoDeleted:
                return "recipe camera: photo deleted";
            case CameraEvent.PhotoKept:
                return "recipe camera: photo not deleted";
            case CameraEvent.PermissionDenied:
                return "recipe camera: permission denied";
            default:
                var name = failure?.GetType().Name ?? "NoError";
                return $"recipe camera: read failed {name}";
        }
    }
}
