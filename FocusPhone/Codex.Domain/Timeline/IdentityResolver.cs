namespace Codex.Domain.Timeline;

/// <summary>
/// Maps a normalized contact key (phone or email) to a stable per-person thread
/// id, so a person you both text and email lands in one conversation.
/// </summary>
public interface IIdentityResolver
{
    string ResolveThreadKey(string contactKey);
}

/// <summary>
/// The trivial resolver: every key is its own person. Preserves the original
/// group-by-contact-key behavior for callers that have no contact data.
/// </summary>
public sealed class PerKeyIdentityResolver : IIdentityResolver
{
    public static readonly PerKeyIdentityResolver Instance = new();

    public string ResolveThreadKey(string contactKey) => contactKey;
}

/// <summary>
/// Resolves person identity from three inputs, in the owner's chosen order of
/// authority (Contacts + manual link, with unlink winning):
///   • associations — groups of keys that belong to one device contact card
///     (auto-merge from Contacts),
///   • manual links — pairs the owner explicitly joined,
///   • standalone keys — keys the owner explicitly unlinked, which are pinned to
///     themselves and never absorbed by an association or link.
/// Keys are unioned (an association group or a link pair share an id) unless a
/// member is pinned standalone. A component's canonical id is its
/// lexicographically smallest key, so resolution is deterministic and stable.
/// </summary>
public sealed class IdentityMap : IIdentityResolver
{
    private readonly Dictionary<string, string> parent = [];
    private readonly HashSet<string> standalone;

    public IdentityMap(
        IEnumerable<IReadOnlyCollection<string>> associations,
        IEnumerable<(string, string)> manualLinks,
        IEnumerable<string> standaloneKeys)
    {
        standalone = [.. standaloneKeys];

        foreach (var group in associations)
        {
            UnionAll(group);
        }
        foreach (var (a, b) in manualLinks)
        {
            UnionAll([a, b]);
        }
    }

    public string ResolveThreadKey(string contactKey)
    {
        if (standalone.Contains(contactKey) || !parent.ContainsKey(contactKey))
        {
            return contactKey;
        }
        return Find(contactKey);
    }

    private void UnionAll(IReadOnlyCollection<string> keys)
    {
        string? anchor = null;
        foreach (var key in keys)
        {
            if (standalone.Contains(key))
            {
                continue; // Pinned: never merged into anyone else's thread.
            }
            EnsureNode(key);
            if (anchor is null)
            {
                anchor = key;
            }
            else
            {
                Link(anchor, key);
            }
        }
    }

    private void EnsureNode(string key)
    {
        if (!parent.ContainsKey(key))
        {
            parent[key] = key;
        }
    }

    private string Find(string key)
    {
        var root = key;
        while (parent[root] != root)
        {
            root = parent[root];
        }
        // Path compression, and keep the smallest key as the canonical root.
        while (parent[key] != root)
        {
            var next = parent[key];
            parent[key] = root;
            key = next;
        }
        return root;
    }

    private void Link(string a, string b)
    {
        var rootA = Find(a);
        var rootB = Find(b);
        if (rootA == rootB)
        {
            return;
        }
        // Smallest key wins as canonical so ids are stable regardless of order.
        var (keep, absorb) = string.CompareOrdinal(rootA, rootB) <= 0 ? (rootA, rootB) : (rootB, rootA);
        parent[absorb] = keep;
    }
}
