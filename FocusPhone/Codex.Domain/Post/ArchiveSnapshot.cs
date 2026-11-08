using System.IO.Compression;
using System.Text;
using System.Text.Json;

using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>The filed archive as one backed-up whole.</summary>
public sealed record ArchiveSnapshot(IReadOnlyList<Ghost> Ghosts, DateTimeOffset TakenUtc)
{
    /// <summary>
    /// Messages taken back out of Filed, and when. The backup is a union, so without this an unfiled message
    /// would come back from Drive on the next sync. Absent in backups from before there was one.
    /// </summary>
    public IReadOnlyList<UnfiledGhost> Unfiled { get; init; } = [];
}

/// <summary>A message the owner took back out of Filed at <paramref name="UnfiledAtUtc"/>.</summary>
public sealed record UnfiledGhost(string SourceId, DateTimeOffset UnfiledAtUtc);

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
/// How the phone's archive and the backup meet. It used to be a one-writer mirror, so whatever the phone held
/// replaced the backup — and a phone whose restore had not run (mail entered by hand after a reset, or offline at
/// first launch) replaced every filed message with the first one filed after (error-handling audit §2). It is a
/// union now, keyed by <see cref="TimelineItem.SourceId"/>, taken in both directions on every sync: what the
/// backup holds and the phone does not is restored, what the phone holds is added to the backup. A message the
/// owner unfiled stays out, because the snapshot remembers when it was unfiled, and a ghost survives unless it
/// was unfiled after it was filed.
/// </summary>
public static class ArchiveSync
{
    public static ArchiveSnapshot Merge(ArchiveSnapshot local, ArchiveSnapshot? backedUp)
    {
        var unfiled = new Dictionary<string, UnfiledGhost>(StringComparer.Ordinal);
        foreach (var entry in local.Unfiled.Concat(backedUp?.Unfiled ?? []))
        {
            if (!unfiled.TryGetValue(entry.SourceId, out var seen) || entry.UnfiledAtUtc > seen.UnfiledAtUtc)
            {
                unfiled[entry.SourceId] = entry;
            }
        }

        // The phone's copy first, so it wins for a message both hold.
        var ghosts = new Dictionary<string, Ghost>(StringComparer.Ordinal);
        foreach (var ghost in local.Ghosts.Concat(backedUp?.Ghosts ?? []))
        {
            ghosts.TryAdd(ghost.Item.SourceId, ghost);
        }

        var kept = ghosts.Values
            .Where(ghost => !unfiled.TryGetValue(ghost.Item.SourceId, out var taken) || ghost.FiledAtUtc > taken.UnfiledAtUtc)
            .OrderByDescending(ghost => ghost.FiledAtUtc)
            .ToList();

        return new ArchiveSnapshot(kept, local.TakenUtc)
        {
            Unfiled = unfiled.Values.OrderBy(entry => entry.SourceId, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>The ghosts the merge holds that the phone does not: what a restore puts back.</summary>
    public static IReadOnlyList<Ghost> ToRestore(ArchiveSnapshot merged, ArchiveSnapshot local)
    {
        var held = local.Ghosts.Select(ghost => ghost.Item.SourceId).ToHashSet(StringComparer.Ordinal);
        return merged.Ghosts.Where(ghost => !held.Contains(ghost.Item.SourceId)).ToList();
    }

    /// <summary>
    /// Whether the merge says something the backup does not. Compared by content, not by when it was taken: the
    /// timestamp changes on every sync and would make every run a write. Nothing at all is never written.
    /// </summary>
    public static bool NeedsWriting(ArchiveSnapshot merged, ArchiveSnapshot? backedUp)
    {
        if (merged.Ghosts.Count == 0 && merged.Unfiled.Count == 0)
        {
            return false;
        }
        if (backedUp is null)
        {
            return true;
        }

        return !Ids(merged.Ghosts.Select(g => g.Item.SourceId)).SequenceEqual(Ids(backedUp.Ghosts.Select(g => g.Item.SourceId)), StringComparer.Ordinal)
            || !Ids(merged.Unfiled.Select(u => u.SourceId)).SequenceEqual(Ids(backedUp.Unfiled.Select(u => u.SourceId)), StringComparer.Ordinal);
    }

    private static IEnumerable<string> Ids(IEnumerable<string> ids) => ids.Order(StringComparer.Ordinal);
}
