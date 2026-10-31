using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;

namespace Constitution.MealsTab;

/// <summary>
/// Lets the camera app write the photograph of one recipe card page into the app's cache and nowhere else (C-6.5.2, D105). A bare
/// file path handed to another app is refused on any modern Android; a provider address grants write access to exactly the one file
/// named, for as long as the camera lives. Not exported. Its paths file (capture_paths.xml) serves the capture folder alone, so the
/// files directory (the household's records, the vault) and public storage are never reachable through it.
/// </summary>
[ContentProvider(
    ["${applicationId}" + RecipeCaptureProvider.AuthoritySuffix],
    Exported = false,
    GrantUriPermissions = true)]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/capture_paths")]
public class RecipeCaptureProvider : AndroidX.Core.Content.FileProvider
{
    /// <summary>
    /// The authority is the application id plus this, so it moves with the frozen app id rather than being another copy of it to
    /// keep in step; the merged manifest is read back by a guard test.
    /// </summary>
    public const string AuthoritySuffix = ".capture";

    public RecipeCaptureProvider()
    {
    }

    protected RecipeCaptureProvider(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer)
    {
    }
}
