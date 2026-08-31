using Android.Content;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Email;

namespace Post;

/// <summary>
/// Fetches an attachment's bytes when the owner asks to open one, and not before.
///
/// They land in the app's <b>cache</b>, never beside the timeline. Post is not a file
/// manager: an inbox that quietly accumulates every picture anyone ever sent is the
/// opposite of a to-do list, and the cache is a directory the system is entitled to
/// reclaim whenever it likes — which is exactly right for a copy that can be re-fetched.
///
/// Shared out through a <c>FileProvider</c> URI rather than a file path, because a bare
/// path to another app is refused on any modern Android and, more to the point, hands over
/// more than the one file.
/// </summary>
internal static class AttachmentCache
{
    private const string AttachmentsFolder = "attachments";

    public static Android.Net.Uri? Fetch(Context context, TimelineItem item, AttachmentRef attachment)
    {
        var directory = new Java.IO.File(context.CacheDir, AttachmentsFolder);
        directory.Mkdirs();

        var safeName = SafeName(attachment.FileName ?? attachment.Id);
        var file = new Java.IO.File(directory, $"{Math.Abs(item.SourceId.GetHashCode())}-{safeName}");

        if (!file.Exists() || file.Length() == 0)
        {
            var bytes = MailAccess.Attachment(context, item, attachment)
                ?? throw new InvalidOperationException("The message no longer carries that attachment.");
            System.IO.File.WriteAllBytes(file.AbsolutePath!, bytes);
        }

        return AndroidX.Core.Content.FileProvider.GetUriForFile(
            context, $"{context.PackageName}.attachments", file);
    }

    // The sender chose the file name. It never becomes a path here: a name with separators
    // in it would otherwise write outside the cache folder entirely.
    private static string SafeName(string name)
    {
        var cleaned = new string(name
            .Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                ? character
                : '_')
            .ToArray());
        return cleaned.Length == 0 ? "attachment" : cleaned[..Math.Min(cleaned.Length, 64)];
    }
}
