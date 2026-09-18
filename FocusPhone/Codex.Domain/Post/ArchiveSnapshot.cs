using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Codex.Domain.Post;

/// <summary>The filed archive as one backed-up whole.</summary>
public sealed record ArchiveSnapshot(IReadOnlyList<Ghost> Ghosts, DateTimeOffset TakenUtc);

/// <summary>
/// The on-Drive wrapper around a gzip-compressed snapshot. A small, versioned shell around
/// the payload rather than raw gzip bytes, because Drive's text seam (<c>IDriveTextFile</c>)
/// only carries strings — base64 keeps this inside that seam rather than standing up a
/// second, binary upload path just for one file. <c>Version</c> exists so a future format
/// change is a decision rather than a guess at what is already sitting in Drive.
/// </summary>
public sealed record ArchiveEnvelope(int Version, string Payload);

/// <summary>
/// Reads and writes the archive backup. Plaintext JSON, deliberately: the full snapshot is
/// still there to be restored by hand if it ever comes to that — <b>compressed</b>, not
/// encrypted, so a passphrase is never required to get it back.
///
/// <b>For the record, and it is the owner's explicit call:</b> this puts the full text of
/// filed correspondence in Drive unencrypted, unlike the vault, which is AES-GCM. Reusing
/// <c>SecretsCrypto</c> with the vault passphrase remains a small change if it is ever
/// reconsidered.
///
/// <b>Why gzip and not a denser JSON shape or a real binary file:</b> a filed email's body
/// dwarfs anything an encoding format could save per entry, so the win has to come from
/// compressing the text itself. Gzip gets most of that win (70-90% on text like this) for a
/// few lines of BCL code and no new dependency; a standalone binary file would need a
/// byte-upload seam this repo's shared Drive glue (<c>IDriveTextFile</c>) doesn't have, and
/// building one just for this file is a bigger lift than the remaining bytes justify.
///
/// <b>Backward compatible by construction:</b> a device with an old, uncompressed backup
/// (written before this shipped) still decodes fine — <see cref="Decode"/> tries the new
/// envelope first and falls back to reading the content as a bare, uncompressed snapshot
/// when it isn't one. Nothing that predates this change is ever treated as corrupt.
/// </summary>
public static class ArchiveSnapshotCodec
{
    /// <summary>The only version written today: the payload is gzip, then base64.</summary>
    public const int GzipBase64Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public static string Encode(ArchiveSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, Options);
        var envelope = new ArchiveEnvelope(GzipBase64Version, Convert.ToBase64String(Compress(json)));
        return JsonSerializer.Serialize(envelope, Options);
    }

    public static ArchiveSnapshot? Decode(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        // The new shape first: a versioned envelope carrying a compressed payload. A file
        // written in the OLD, bare-snapshot shape has no "Payload" property, so this
        // deserializes to a null Payload rather than throwing — which is what sends it to
        // the fallback below instead of being read (wrongly) as an empty envelope.
        try
        {
            var envelope = JsonSerializer.Deserialize<ArchiveEnvelope>(content, Options);
            if (envelope?.Payload is not null)
            {
                var json = Decompress(Convert.FromBase64String(envelope.Payload));
                return JsonSerializer.Deserialize<ArchiveSnapshot>(json, Options);
            }
        }
        catch (JsonException)
        {
        }
        catch (FormatException)
        {
            // Malformed base64 in what otherwise parsed as an envelope: not recoverable as
            // the new shape, but still worth trying as the old one before giving up.
        }
        catch (InvalidDataException)
        {
            // Payload parsed and decoded from base64 but isn't valid gzip.
        }

        // The old shape: a bare, uncompressed snapshot, exactly what every backup written
        // before this change looks like. Read as-is rather than reported as lost.
        try
        {
            return JsonSerializer.Deserialize<ArchiveSnapshot>(content, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static byte[] Compress(string json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
        {
            gzip.Write(Encoding.UTF8.GetBytes(json));
        }
        return output.ToArray();
    }

    private static string Decompress(byte[] gzipBytes)
    {
        using var input = new MemoryStream(gzipBytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}

/// <summary>
/// When to write the archive up, and when to take it down.
///
/// The phone is the single writer, as it is for the audio library, so this is a one-writer
/// mirror rather than a merge. The rule that matters is the refusal: <b>an empty local
/// archive never overwrites a backup</b>. A fresh install has nothing filed yet, and
/// writing that up would destroy the only copy of every message the owner ever filed —
/// which is the exact thing the backup exists to prevent.
/// </summary>
public static class ArchiveSync
{
    public static bool NeedsWriting(ArchiveSnapshot local, ArchiveSnapshot? backedUp)
    {
        // The refusal comes first, before any comparison. Nothing local is not a state
        // worth propagating; it is the state a reset leaves behind.
        if (local.Ghosts.Count == 0)
        {
            return false;
        }
        if (backedUp is null)
        {
            return true;
        }

        // Compared by content, not by when it was taken: the timestamp changes on every
        // sync and would make every run a write.
        return !SameContent(local, backedUp);
    }

    /// <summary>A device with nothing filed takes what Drive has; one with ghosts is the authority.</summary>
    public static bool ShouldRestore(IReadOnlyList<Ghost> local) => local.Count == 0;

    private static bool SameContent(ArchiveSnapshot left, ArchiveSnapshot right)
    {
        if (left.Ghosts.Count != right.Ghosts.Count)
        {
            return false;
        }

        var leftIds = left.Ghosts.Select(ghost => ghost.Item.SourceId).OrderBy(id => id, StringComparer.Ordinal);
        var rightIds = right.Ghosts.Select(ghost => ghost.Item.SourceId).OrderBy(id => id, StringComparer.Ordinal);
        return leftIds.SequenceEqual(rightIds, StringComparer.Ordinal);
    }
}
