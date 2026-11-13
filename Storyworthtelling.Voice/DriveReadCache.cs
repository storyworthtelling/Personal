using System.Collections.Concurrent;

namespace Storyworthtelling.Voice;

/// <summary>
/// What a Drive file held at a given revision, kept in memory so a file whose <c>headRevisionId</c> has not moved
/// is not downloaded again (efficiency audit §3, §12). Drive gives a file a new head revision whenever its content
/// changes, so a hit can never be stale; it only saves the download. A head shares one,
/// <see cref="Shared"/>, across every <see cref="DriveTextFile"/> it builds.
///
/// <para>Bounded both ways: a file larger than <see cref="MaxEntryChars"/> is never kept, and past
/// <see cref="MaxEntries"/> the cache starts again rather than growing. What it holds is what Drive holds —
/// ciphertext for every sealed file — and it lives only as long as the process.</para>
/// </summary>
public sealed class DriveReadCache
{
    public const int MaxEntries = 32;
    public const int MaxEntryChars = 4 * 1024 * 1024;

    private readonly ConcurrentDictionary<string, string> entries = new(StringComparer.Ordinal);

    /// <summary>The one a head passes to every Drive file it builds.</summary>
    public static DriveReadCache Shared { get; } = new();

    public bool TryGet(string fileId, string? revision, out string content)
    {
        content = string.Empty;
        return !string.IsNullOrEmpty(revision) && entries.TryGetValue(Key(fileId, revision), out content!);
    }

    public void Keep(string fileId, string? revision, string content)
    {
        if (string.IsNullOrEmpty(revision) || content.Length > MaxEntryChars)
        {
            return;
        }
        if (entries.Count >= MaxEntries)
        {
            entries.Clear();
        }
        entries[Key(fileId, revision)] = content;
    }

    private static string Key(string fileId, string revision) => fileId + "@" + revision;
}
