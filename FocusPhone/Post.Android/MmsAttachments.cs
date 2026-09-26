using Android.Content;
using Codex.Domain.Timeline;

namespace Post;

/// <summary>
/// Fetches an MMS attachment's bytes. Unlike a Gmail attachment these are already on the
/// device — the download that landed them (<see cref="MmsDownloader"/>) already wrote them
/// into the system MMS provider — so this is a local content read, not a network call.
/// </summary>
internal static class MmsAttachments
{
    private const string PartIdPrefix = "mms-part:";

    public static byte[]? Fetch(Context context, AttachmentRef attachment)
    {
        if (!attachment.Id.StartsWith(PartIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }
        var partId = attachment.Id[PartIdPrefix.Length..];

        using var stream = context.ContentResolver?.OpenInputStream(
            Android.Net.Uri.Parse($"content://mms/part/{partId}")!);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
