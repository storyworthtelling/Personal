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

    public InMemoryDriveFileIdStore(IReadOnlyDictionary<string, string>? seed = null) =>
        ids = seed is null ? new() : new(seed);

    public string? Get(string fileName) => ids.TryGetValue(fileName, out var id) ? id : null;

    public void Set(string fileName, string fileId) => ids[fileName] = fileId;

    /// <summary>The current mapping, for persisting back into the vault's DriveFileIds section.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(ids);
}
