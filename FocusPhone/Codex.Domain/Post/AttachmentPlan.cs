using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>What Post should do with one attachment.</summary>
public enum AttachmentAction
{
    /// <summary>Show it here. Only for things Post can honestly render.</summary>
    ShowInline,

    /// <summary>Open it in Plate, the suite's photo app.</summary>
    OpenInPlate,

    /// <summary>Hand it to whatever on the phone can open it.</summary>
    HandOff,

    /// <summary>Do not open it, and say why.</summary>
    Refuse,
}

/// <summary>
/// What to do with an attachment, decided here rather than on the device.
///
/// Post is <b>not a file manager</b> — the owner's rule — so an attachment is viewed, or
/// handed to the app whose job it is, and never accumulated. A picture goes to Plate
/// because the suite has its own photo app and handing a picture to a generic viewer is
/// exactly the seam this phone exists to remove.
///
/// The refusals matter more than the openings. A stranger chose the file's name, its
/// extension and its declared type, so a disagreement between them is resolved toward the
/// more dangerous reading rather than the more convenient one, and an installer is never
/// something a phone with sideloading switched off should offer to open.
/// </summary>
public static class AttachmentPlan
{
    /// <summary>Past this, an attachment is handed off rather than opened in Post.</summary>
    public const long InlineLimitBytes = 10L * 1024 * 1024;

    private static readonly string[] RefusedTypes =
    [
        "application/vnd.android.package-archive",
        "application/x-msdownload",
        "application/x-msdos-program",
        "application/x-sh",
        "application/octet-stream",
    ];

    private static readonly string[] RefusedExtensions =
        [".apk", ".exe", ".bat", ".cmd", ".sh", ".scr", ".msi", ".jar"];

    public static AttachmentAction For(AttachmentRef attachment)
    {
        var mime = attachment.MimeType ?? string.Empty;
        var name = attachment.FileName ?? string.Empty;

        // The name and the type were both chosen by whoever sent it, so a file called
        // .apk is refused however innocently it declares itself — and vice versa.
        if (RefusedTypes.Contains(mime, StringComparer.OrdinalIgnoreCase)
            || RefusedExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            return AttachmentAction.Refuse;
        }

        if (attachment.SizeBytes > InlineLimitBytes)
        {
            return AttachmentAction.HandOff;
        }

        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentAction.OpenInPlate;
        }

        if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentAction.ShowInline;
        }

        return AttachmentAction.HandOff;
    }

    /// <summary>A count in words, because a MIME type is not something to put on a button.</summary>
    public static string Label(IReadOnlyList<AttachmentRef> attachments) =>
        attachments.Count == 1 ? "1 attachment" : $"{attachments.Count} attachments";
}
