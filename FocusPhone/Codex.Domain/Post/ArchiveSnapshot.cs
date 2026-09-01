using System.Text.Json;

namespace Codex.Domain.Post;

/// <summary>The filed archive as one backed-up whole.</summary>
public sealed record ArchiveSnapshot(IReadOnlyList<Ghost> Ghosts, DateTimeOffset TakenUtc);

/// <summary>
/// Reads and writes the archive backup. Plaintext JSON, deliberately: one file, found by
/// name, restorable by hand if it ever comes to that.
///
/// <b>For the record, and it is the owner's explicit call:</b> this puts the full text of
/// filed correspondence in Drive unencrypted, unlike the vault, which is AES-GCM. Reusing
/// <c>SecretsCrypto</c> with the vault passphrase remains a small change if it is ever
/// reconsidered.
/// </summary>
public static class ArchiveSnapshotCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string Encode(ArchiveSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static ArchiveSnapshot? Decode(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ArchiveSnapshot>(content, Options);
        }
        catch (JsonException)
        {
            return null;
        }
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
