using System.Collections.Concurrent;

namespace Storyworthtelling.Voice;

/// <summary>
/// The default <see cref="IDriveFileIdStore"/>: an in-memory, thread-safe map an app seeds from the vault's
/// <c>DriveFileIds</c> section at startup and reads back (via <see cref="Snapshot"/>) to persist changes. The
/// vault is the durable home; this is the fast per-op cache the Drive seam consults.
/// </summary>
public sealed class InMemoryDriveFileIdStore : IDriveFileIdStore
{
    private readonly ConcurrentDictionary<string, string> ids;
    private readonly Action<IReadOnlyDictionary<string, string>>? onChange;

    /// <param name="seed">Initial ids (e.g. from a local cache or the vault).</param>
    /// <param name="onChange">
    /// Fired with the full snapshot whenever a NEW or CHANGED id is captured, so an app can persist the
    /// map eagerly (to local Preferences) rather than only on the next vault publish. This closes the gap
    /// where an id captured in one session is lost on restart before a publish, so a file the owner
    /// renamed between sessions was re-created as a duplicate. Best-effort; never called for a no-op Set.
    /// </param>
    public InMemoryDriveFileIdStore(
        IReadOnlyDictionary<string, string>? seed = null,
        Action<IReadOnlyDictionary<string, string>>? onChange = null)
    {
        ids = seed is null ? new() : new(seed);
        this.onChange = onChange;
    }

    public string? Get(string fileName) => ids.TryGetValue(fileName, out var id) ? id : null;

    public void Set(string fileName, string fileId)
    {
        var changed = !ids.TryGetValue(fileName, out var existing) || existing != fileId;
        ids[fileName] = fileId;
        if (changed)
        {
            onChange?.Invoke(Snapshot());
        }
    }

    /// <summary>The current mapping, for persisting back into the vault's DriveFileIds section.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(ids);
}
