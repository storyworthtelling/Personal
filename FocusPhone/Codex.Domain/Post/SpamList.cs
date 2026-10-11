using System.Text.RegularExpressions;
using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// What the owner has judged spam: the numbers and addresses that sent it, and the wording
/// of what they sent. Anything matching either is hidden from the Mailbox for good — the
/// owner decided once, so it does not come back as a new correspondence.
///
/// Two deliberately different rules:
/// <list type="bullet">
/// <item>A <b>sender</b> match is exact, after normalization. It means "that number, one to
/// one". A group the number belongs to is a conversation with other people in it, so a
/// group is judged on its wording alone.</item>
/// <item>A <b>wording</b> match is near-identical, not similar. Spam rotates the numbers and
/// figures in a message and keeps its words, so digit runs are read as one token and the
/// rest must agree closely. A short message only matches exactly: "Call me back" must never
/// swallow "Call me back now" from someone the owner actually knows.</item>
/// </list>
/// </summary>
public sealed class SpamList
{
    /// <summary>
    /// Fewest distinct words a message needs before a near match is allowed at all. Below
    /// this, one shared word is most of the message and the match means nothing.
    /// </summary>
    public const int FuzzyWordFloor = 4;

    /// <summary>
    /// Shared words over all distinct words, at or above which two messages count as the
    /// same wording. Two words changing in a seven-word message is a different message.
    /// </summary>
    public const double NearIdenticalShare = 0.8;

    private const string SenderPrefix = "sender:";
    private const string TextPrefix = "text:";

    private static readonly Regex DigitRun = new(@"\d+", RegexOptions.CultureInvariant);
    private static readonly Regex WordBreak = new(@"[^\p{L}#]+", RegexOptions.CultureInvariant);

    private readonly HashSet<string> senders = new(StringComparer.Ordinal);
    private readonly HashSet<string> texts = new(StringComparer.Ordinal);

    /// <summary>Rebuilds the list from its stored form (see <see cref="ToStored"/>).</summary>
    public static SpamList From(IEnumerable<string> stored)
    {
        var list = new SpamList();
        foreach (var entry in stored)
        {
            if (entry.StartsWith(SenderPrefix, StringComparison.Ordinal))
            {
                list.senders.Add(entry[SenderPrefix.Length..]);
            }
            else if (entry.StartsWith(TextPrefix, StringComparison.Ordinal))
            {
                list.texts.Add(entry[TextPrefix.Length..]);
            }
        }
        return list;
    }

    /// <summary>
    /// The list as flat strings, for a preference set. Each entry carries its kind in a
    /// prefix so one set can hold both without a second key.
    /// </summary>
    public IReadOnlyList<string> ToStored() =>
        senders.Select(sender => SenderPrefix + sender)
            .Concat(texts.Select(text => TextPrefix + text))
            .ToList();

    /// <summary>
    /// Judges these items spam. The sender of each one-to-one item is recorded, and the
    /// wording of each that has any.
    /// </summary>
    public void Mark(IEnumerable<TimelineItem> items)
    {
        foreach (var item in items)
        {
            if (!item.IsGroup)
            {
                senders.Add(Sender(item));
            }
            var fingerprint = Fingerprint(Wording(item));
            if (fingerprint.Length > 0)
            {
                texts.Add(fingerprint);
            }
        }
    }

    /// <summary>
    /// Takes back what <see cref="Mark"/> recorded for these items. Only correct for items
    /// that were not already spam before being marked — which the Mailbox guarantees, since
    /// a correspondence that matches the list is already hidden and cannot be marked again.
    /// </summary>
    public void Unmark(IEnumerable<TimelineItem> items)
    {
        foreach (var item in items)
        {
            if (!item.IsGroup)
            {
                senders.Remove(Sender(item));
            }
            var fingerprint = Fingerprint(Wording(item));
            if (fingerprint.Length > 0)
            {
                texts.Remove(fingerprint);
            }
        }
    }

    /// <summary>Whether this item is spam by sender or by wording.</summary>
    public bool Matches(TimelineItem item)
    {
        if (!item.IsGroup && senders.Contains(Sender(item)))
        {
            return true;
        }

        var fingerprint = Fingerprint(Wording(item));
        return fingerprint.Length > 0
            && texts.Any(spam => NearIdentical(spam, fingerprint));
    }

    /// <summary>The items that are not spam, in their original order.</summary>
    public IEnumerable<TimelineItem> Filter(IEnumerable<TimelineItem> items) =>
        items.Where(item => !Matches(item));

    /// <summary>
    /// The words a message is judged by. Mail is its subject and body (a newsletter's
    /// wording lives in both); anything else is its text.
    /// </summary>
    public static string Wording(TimelineItem item) =>
        item.Kind == TimelineItemKind.Email
            ? $"{item.Subject} {item.Body ?? item.Snippet}"
            : item.Snippet ?? string.Empty;

    /// <summary>
    /// The comparable form of a message: lower case, every run of digits as one token, and
    /// words separated by anything that is not a letter. Empty for a message with no words.
    /// </summary>
    public static string Fingerprint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var folded = DigitRun.Replace(text.ToLowerInvariant(), "#");
        var words = WordBreak.Split(folded).Where(word => word.Length > 0);
        return string.Join(' ', words);
    }

    /// <summary>
    /// Whether two fingerprints are the same message worded almost identically. Exact
    /// equality always counts; otherwise both must have enough distinct words for a share
    /// of them to mean anything.
    /// </summary>
    public static bool NearIdentical(string spam, string candidate)
    {
        if (spam == candidate)
        {
            return true;
        }

        var spamWords = spam.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var candidateWords = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (spamWords.Count < FuzzyWordFloor || candidateWords.Count < FuzzyWordFloor)
        {
            return false;
        }

        var shared = spamWords.Intersect(candidateWords).Count();
        var distinct = spamWords.Union(candidateWords).Count();
        return (double)shared / distinct >= NearIdenticalShare;
    }

    private static string Sender(TimelineItem item) => ContactKeyNormalizer.NormalizeAny(item.ContactKey);
}

/// <summary>
/// Whether a correspondence's sender is already in the owner's address book. Only someone
/// who is not in the book is offered the choice between adding them and marking them spam.
/// </summary>
public static class RolodexMembership
{
    /// <param name="bookAddresses">Every phone number and email address in the book, as written.</param>
    /// <param name="senderKeys">The correspondence's keys, as the Mailbox holds them (normalized).</param>
    /// <returns>True when any of the correspondence's keys is an address in the book.</returns>
    public static bool IsKnown(IEnumerable<string> bookAddresses, IEnumerable<string> senderKeys)
    {
        var known = bookAddresses.Select(ContactKeyNormalizer.NormalizeAny).ToHashSet();
        return senderKeys.Any(key => known.Contains(ContactKeyNormalizer.NormalizeAny(key)));
    }
}
