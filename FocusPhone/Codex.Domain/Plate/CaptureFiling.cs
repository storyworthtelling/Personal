namespace Codex.Domain.Plate;

/// <summary>
/// One row's worth of MediaStore columns: where a capture is filed, and under what name.
/// A record rather than three loose strings so a caller cannot pair a name with the wrong
/// folder, which is exactly the mistake this type exists to have fixed.
/// </summary>
public readonly record struct MediaStoreEntry(string DisplayName, string MimeType, string RelativePath);

/// <summary>
/// Where Plate's captures go.
///
/// <para><b>The bug this fixes.</b> The shipped Camera head inserted into
/// <c>MediaStore.Images.Media.ExternalContentUri</c> with a display name and a MIME type
/// and nothing else. With <c>RELATIVE_PATH</c> unset the Images collection files under its
/// default primary directory, <c>Pictures/</c> — and Google Photos automatically backs up
/// <c>DCIM/Camera</c> while treating every other device folder as opt-in. So every photo
/// that app ever took has been on the phone and outside backup, which is what the owner
/// reported as "pictures don't go directly into Google Photos".</para>
///
/// <para>The other half: scans were written to <c>MediaStore.Downloads</c>, reported as
/// "scans vanish into Downloads". A scan is a document and is filed with documents.</para>
///
/// <para>This lives in the Domain because the head cannot be compiled in the sandbox —
/// its ML Kit bindings fetch from dl.google.com at build time — so a folder name typed
/// straight into an Activity would be unverifiable until it reached a phone.</para>
/// </summary>
public static class CaptureFiling
{
    /// <summary>
    /// The one folder Google Photos backs up without being asked. Everything the owner
    /// shoots goes here; nothing else does.
    /// </summary>
    public const string CameraRoll = "DCIM/Camera";

    /// <summary>Scans are documents, so they file with documents — never in the roll.</summary>
    public const string ScanFolder = "Documents/Plate";

    private const string Prefix = "Plate";

    /// <summary>A photo, named so a burst sorts in the order it was shot.</summary>
    public static MediaStoreEntry Photo(DateTime takenAt, int sequence = 0) =>
        new(Name(takenAt, sequence, "jpg"), "image/jpeg", CameraRoll);

    /// <summary>A video. Same roll as the photos: it is a moment like any other.</summary>
    public static MediaStoreEntry Video(DateTime takenAt, int sequence = 0) =>
        new(Name(takenAt, sequence, "mp4"), "video/mp4", CameraRoll);

    /// <summary>
    /// A finished scan. Titled rather than timestamped, because the owner named it and a
    /// document is looked for by name; the date disambiguates two claims for the same thing.
    /// </summary>
    public static MediaStoreEntry Scan(string title, DateTime scannedAt) =>
        new($"{Safe(title)} — {scannedAt:yyyy-MM-dd}.pdf", "application/pdf", ScanFolder);

    // MediaStore silently renames a collision to "name (1).ext", which breaks the promise
    // that these sort in capture order. A burst is the ordinary case, so the name stays
    // unique at the resolution people actually shoot at.
    private static string Name(DateTime takenAt, int sequence, string extension) =>
        sequence <= 0
            ? $"{Prefix}_{takenAt:yyyyMMdd_HHmmss}.{extension}"
            : $"{Prefix}_{takenAt:yyyyMMdd_HHmmss}_{sequence + 1}.{extension}";

    // A scan title is typed by the owner and ends up as a filename. A slash would nest the
    // file somewhere else entirely; the rest are reserved on volumes MediaStore writes to.
    private static string Safe(string title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "Scan";
        }

        var cleaned = new string(trimmed.Select(character =>
            character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '-' : character).ToArray());
        return cleaned.Trim().Length == 0 ? "Scan" : cleaned.Trim();
    }
}
