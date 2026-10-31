using Android.App;
using Android.Content;
using Android.Provider;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>
/// The platform camera for one recipe card page (C-6.5.2, D105): the system's own capture screen, writing into the one cache file
/// <see cref="CameraCaptureCache"/> names, served by <see cref="RecipeCaptureProvider"/>. Nothing is written to public storage or
/// the media library, and the file is cleared before every capture, when a capture is backed out of, and by the reader once it has
/// the words, so at most one photograph exists and only while it is being read. Decides nothing else: whether to ask for the camera
/// permission, and what a result means, are the area's and the domain's.
/// </summary>
internal sealed class CameraCaptureLauncher(Activity host, int answerCode = CameraCaptureLauncher.RequestCode, string? targetPackage = null)
{
    /// <summary>The request code the camera's answer comes back under. Health uses 7311, Plate's scan 7412.</summary>
    public const int RequestCode = 7413;

    /// <summary>The request code the camera permission's answer comes back under.</summary>
    public const int PermissionRequestCode = 7414;

    /// <summary>Where the photograph is written.</summary>
    public string PhotoPath => CameraCaptureCache.PathIn(host.CacheDir!.AbsolutePath);

    /// <summary>
    /// Opens the camera. Null when it opened; otherwise the sentence for why it did not. A photograph left from an earlier capture
    /// is cleared first, so the camera never starts on top of someone's old page. A launcher given a package aims at that app's
    /// camera first (Plate on the owner's phone) and falls back to whatever camera the phone has when that app is not there; the
    /// answer comes back under the code the launcher was given, so two flows sharing this route cannot read each other's answer.
    /// </summary>
    public string? Start()
    {
        Sweep();
        var folder = new Java.IO.File(CameraCaptureCache.FolderIn(host.CacheDir!.AbsolutePath));
        folder.Mkdirs();
        var file = new Java.IO.File(PhotoPath);
        var address = AndroidX.Core.Content.FileProvider.GetUriForFile(host, host.PackageName + RecipeCaptureProvider.AuthoritySuffix, file);

        var intent = new Intent(MediaStore.ActionImageCapture);
        intent.PutExtra(MediaStore.ExtraOutput, address);
        intent.AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
        if (targetPackage is not null)
        {
            intent.SetPackage(targetPackage);
            try
            {
                host.StartActivityForResult(intent, answerCode);
                return null;
            }
            catch (ActivityNotFoundException)
            {
                // That app is not on this phone: the plain camera intent below is the fallback, not a failure.
                intent.SetPackage(null);
            }
        }

        try
        {
            host.StartActivityForResult(intent, answerCode);
            return null;
        }
        catch (ActivityNotFoundException)
        {
            // No camera app answered. The folder is empty (swept above), so there is nothing to delete.
            return CameraAccess.CaptureFailedSentence;
        }
    }

    /// <summary>Removes the photograph, if there is one. The camera was backed out of, or never wrote it.</summary>
    public void Discard() => Delete(PhotoPath);

    /// <summary>Clears the capture folder of anything left behind, such as a photograph taken just before the phone ended the app.</summary>
    public void Sweep() => SweepLeftovers(host);

    /// <summary>
    /// The sweep without a launcher: the app runs it once as it opens (D162), so a photograph of a card left by a phone that ended the app is gone even if no capture is ever started again.
    /// A file that stays is on the trail as the one fixed phrase for a photograph not deleted; the next capture, or the next start, tries again.
    /// </summary>
    public static void SweepLeftovers(Context context)
    {
        var stayed = CameraCaptureCache.Sweep(context.CacheDir!.AbsolutePath, Delete);
        if (stayed.Count > 0)
        {
            new Constitution.Services.ConstitutionVaultLog(context).Outcome(RecipeCameraTrail.LogPhrase(CameraEvent.PhotoKept, null));
        }
    }

    /// <summary>
    /// Deletes a file; true when it is gone (including when it was never there). A file that cannot be deleted says so rather than
    /// being reported gone, and the next capture sweeps it again.
    /// </summary>
    public static bool Delete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
