using Android.Content;
using Android.Runtime;

namespace Post;

/// <summary>
/// Shares one fetched attachment with the app that will open it.
///
/// A bare file path handed to another app is refused on any modern Android, and would in
/// any case hand over more than the one file. A provider URI grants read access to exactly
/// what was named, for as long as the receiving activity lives.
///
/// Its authority is derived from the package name, so it moves with the frozen app id
/// rather than being another literal to keep in step. The paths file scopes it to the
/// attachments cache folder alone — the app's files directory holds timeline.db and the
/// ghost archive, and neither is anybody else's business.
/// </summary>
[ContentProvider(
    ["${applicationId}.attachments"],
    Name = "com.storyworthtelling.focusphone.communication.AttachmentProvider",
    Exported = false,
    GrantUriPermissions = true)]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/file_paths")]
public class AttachmentProvider : AndroidX.Core.Content.FileProvider
{
    public AttachmentProvider()
    {
    }

    protected AttachmentProvider(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer)
    {
    }
}
