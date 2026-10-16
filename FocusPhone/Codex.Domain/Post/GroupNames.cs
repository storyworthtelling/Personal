using System.Text;
using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// What each group conversation is called. Two layers: the name the owner gave it, and the
/// name Google Messages calls it (learned from its notifications). The owner's always wins;
/// with neither, the Mailbox falls back to listing the members.
///
/// Keyed by the thread key (<see cref="KeyFor"/>), the same string the Mailbox's rows carry,
/// so a name follows the conversation however its members change. Kept on the phone only.
/// </summary>
public sealed class GroupNames
{
    /// <summary>Longest name kept; a group name is a title, and a row has one line for it.</summary>
    public const int MaxLength = 60;

    private const char Separator = '\t';

    public static readonly GroupNames Empty = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal));

    private readonly Dictionary<string, string> named;
    private readonly Dictionary<string, string> learned;

    private GroupNames(Dictionary<string, string> named, Dictionary<string, string> learned)
    {
        this.named = named;
        this.learned = learned;
    }

    /// <summary>The thread key for a group id — the key the Mailbox's group rows carry.</summary>
    public static string KeyFor(string groupId) => TimelineMerger.GroupThreadKey(groupId);

    public static GroupNames From(IEnumerable<string> namedStored, IEnumerable<string> learnedStored) =>
        new(Parse(namedStored), Parse(learnedStored));

    public string? TitleFor(string threadKey) =>
        named.TryGetValue(threadKey, out var name) ? name
        : learned.TryGetValue(threadKey, out var title) ? title
        : null;

    public string? OwnersNameFor(string threadKey) => named.GetValueOrDefault(threadKey);

    /// <summary>Gives a group the owner's name for it; a blank name takes it away again.</summary>
    public GroupNames Name(string threadKey, string? name)
    {
        var copy = new Dictionary<string, string>(named, StringComparer.Ordinal);
        var clean = Clean(name);
        if (clean is null)
        {
            copy.Remove(threadKey);
        }
        else
        {
            copy[threadKey] = clean;
        }
        return new GroupNames(copy, learned);
    }

    /// <summary>
    /// Records what Google Messages calls the group. Returns this same instance when nothing
    /// changed, so the caller can skip a write on every repeated notification.
    /// </summary>
    public GroupNames Learn(string threadKey, string? title)
    {
        var clean = Clean(title);
        if (clean is null || (learned.TryGetValue(threadKey, out var known) && known == clean))
        {
            return this;
        }
        var copy = new Dictionary<string, string>(learned, StringComparer.Ordinal) { [threadKey] = clean };
        return new GroupNames(named, copy);
    }

    public IReadOnlyList<string> NamedStored() => Store(named);

    public IReadOnlyList<string> LearnedStored() => Store(learned);

    private static List<string> Store(Dictionary<string, string> names) =>
        names.Select(pair => pair.Key + Separator + pair.Value).ToList();

    private static Dictionary<string, string> Parse(IEnumerable<string> stored)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in stored)
        {
            var at = entry.IndexOf(Separator);
            if (at <= 0)
            {
                continue;
            }
            var name = Clean(entry[(at + 1)..]);
            if (name is not null)
            {
                names[entry[..at]] = name;
            }
        }
        return names;
    }

    // One line, single-spaced, trimmed and capped; null when nothing is left.
    private static string? Clean(string? name)
    {
        if (name is null)
        {
            return null;
        }
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            var ch = char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c;
            if (ch == ' ' && (builder.Length == 0 || builder[^1] == ' '))
            {
                continue;
            }
            builder.Append(ch);
        }
        var clean = builder.ToString().TrimEnd();
        if (clean.Length > MaxLength)
        {
            clean = clean[..MaxLength].TrimEnd();
        }
        return clean.Length == 0 ? null : clean;
    }
}
