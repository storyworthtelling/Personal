using System.IO.Compression;
using System.Text;

namespace Storyworthtelling.Updates;

/// <summary>
/// Reads an APK's form from its own compiled manifest: the reading taken from the bytes that would actually be
/// installed, rather than from anything published beside them (P14 W-3.1.1).
///
/// A compiled manifest is Android's binary XML, whose string pool holds every name the manifest uses, so a manifest
/// that declares <c>android.hardware.type.watch</c> carries that string, in UTF-16 (what aapt2 writes) or UTF-8. Finding
/// it anywhere in the manifest reads as a watch APK: the one way that could be wrong is a phone APK that mentions the
/// feature, which is then refused on the phone, visibly, rather than a watch APK installed over a phone app. BCL only,
/// so the phone heads and, later, the watch's installer read the same way.
/// </summary>
public static class ApkManifest
{
    public const string EntryName = "AndroidManifest.xml";

    // A compiled manifest is a few kilobytes; one past this is not a manifest, and is not read into memory.
    private const int MaxManifestBytes = 4 * 1024 * 1024;

    private static readonly byte[] Utf16 = Encoding.Unicode.GetBytes(ApkForms.WatchFeature);

    private static readonly byte[] Utf8 = Encoding.UTF8.GetBytes(ApkForms.WatchFeature);

    /// <summary>
    /// The form the APK's manifest declares, or <see cref="ApkForm.Unknown"/> when the stream is not an APK (not a zip,
    /// or no manifest). The stream must be seekable; it is left open at the position it was given at.
    /// </summary>
    public static ApkForm FormOf(Stream apk)
    {
        var start = apk.Position;
        try
        {
            var manifest = ReadManifest(apk);
            if (manifest is null)
            {
                return ApkForm.Unknown;
            }
            var span = manifest.AsSpan();
            return span.IndexOf(Utf16) >= 0 || span.IndexOf(Utf8) >= 0 ? ApkForm.Watch : ApkForm.Phone;
        }
        catch (InvalidDataException)
        {
            // Not a zip, or a damaged one: nothing here says what it is, so it is installed nowhere.
            return ApkForm.Unknown;
        }
        finally
        {
            apk.Position = start;
        }
    }

    private static byte[]? ReadManifest(Stream apk)
    {
        using var zip = new ZipArchive(apk, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry(EntryName);
        if (entry is null || entry.Length > MaxManifestBytes)
        {
            return null;
        }

        using var content = entry.Open();
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
        {
            copy.Write(buffer, 0, read);
            if (copy.Length > MaxManifestBytes)
            {
                return null;
            }
        }
        return copy.ToArray();
    }
}
