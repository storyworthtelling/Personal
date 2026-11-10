using System.Collections.Concurrent;

namespace Storyworthtelling.Diagnostics;

/// <summary>
/// The first refusal of each Android permission in a process, as one line (audit 08 §24). The read that needs it is
/// retried on every reload, so a line each time would roll the log, and none at all left an empty screen unexplained.
/// </summary>
public sealed class PermissionDenials
{
    private readonly ConcurrentDictionary<string, bool> said = new(StringComparer.Ordinal);

    /// <summary>The line to log the first time this permission is refused; null after that.</summary>
    public string? First(string permission) =>
        said.TryAdd(permission, true) ? $"PERMISSION {permission} denied" : null;
}
