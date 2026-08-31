namespace Codex.Domain.Post;

/// <summary>
/// What a piece of mail is. Only <see cref="Human"/> waits indefinitely; the rest is
/// trivia with a lifetime, except a bill, which never ages out because missing one costs
/// real money.
/// </summary>
public enum MailClass
{
    /// <summary>From a person. Waits until dealt with, forever if need be.</summary>
    Human,

    /// <summary>Marketing. The shortest life of anything in the Mailbox.</summary>
    Marketing,

    /// <summary>A newsletter — worth a week, not worth keeping.</summary>
    Newsletter,

    /// <summary>A bill or statement. Never ages out, and shows its amount and due date.</summary>
    Bill,

    /// <summary>A receipt, a code, a confirmation. Useful briefly and then not at all.</summary>
    Transactional,
}

/// <summary>What the rules get to look at. Deliberately small: headers and a subject.</summary>
public sealed record MailSignals(string FromAddress, string? Subject, bool HasListUnsubscribe);

/// <summary>
/// What the rules concluded. Two answers, not one, because they are genuinely different
/// questions and conflating them is how a classifier goes wrong: List-Unsubscribe settles
/// that something IS machine mail while saying nothing about which kind of list it is.
/// </summary>
/// <param name="KnownMachine">True when the rules are certain no person sent this.</param>
/// <param name="Kind">A definite classification, or null when only the model can say.</param>
public readonly record struct RuleVerdict(bool KnownMachine, MailClass? Kind);

/// <summary>
/// The cheap half of classification. Rules run first and Claude is asked only about what
/// they cannot place — which keeps the cost down, keeps most mail off the network
/// entirely, and means an absent or rejected key degrades to rules-only rather than to
/// nothing at all.
///
/// A null Kind means "no idea", not "human". Guessing human would be safe for a person and
/// wrong for the Mailbox; guessing trivia would age a real message out in a day. The rules
/// only name a kind when they are sure of it.
/// </summary>
public static class MailRules
{
    // Headers a person's mail client does not send, and addresses that say outright that
    // nobody is listening.
    private static readonly string[] NoReplyMarkers =
        ["no-reply@", "noreply@", "do-not-reply@", "donotreply@", "no_reply@"];

    // A bill is the one kind that never ages out, so it is worth catching by rule rather
    // than leaving to a model that might have an off day.
    private static readonly string[] BillMarkers =
        ["statement", "invoice", "amount due", "payment due", "bill is ready", "your bill"];

    public static RuleVerdict Classify(MailSignals signals)
    {
        var subject = signals.Subject ?? string.Empty;
        var from = signals.FromAddress ?? string.Empty;

        var machine = signals.HasListUnsubscribe
            || NoReplyMarkers.Any(marker => from.Contains(marker, StringComparison.OrdinalIgnoreCase));

        if (BillMarkers.Any(marker => subject.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return new RuleVerdict(KnownMachine: true, MailClass.Bill);
        }

        if (!machine)
        {
            // Not obviously machine mail is NOT the same as human. Saying so would let a
            // model's job be done badly by a rule that never looked at the body.
            return new RuleVerdict(KnownMachine: false, Kind: null);
        }

        // A no-reply address with nothing else to go on is almost always a receipt or a
        // confirmation. A mailing list could be any of several kinds, and which one is
        // exactly the question a header cannot answer — so that goes to the model.
        return signals.HasListUnsubscribe
            ? new RuleVerdict(KnownMachine: true, Kind: null)
            : new RuleVerdict(KnownMachine: true, MailClass.Transactional);
    }
}

/// <summary>
/// How long each kind of trivia lives before it goes. Aged-out trivia is gone entirely,
/// not filed: filing is for things the owner dealt with, and a marketing email nobody
/// opened was never dealt with.
/// </summary>
public static class MailAgeing
{
    public static TimeSpan? Lifetime(MailClass kind) => kind switch
    {
        MailClass.Marketing => TimeSpan.FromDays(1),
        MailClass.Newsletter => TimeSpan.FromDays(7),
        MailClass.Transactional => TimeSpan.FromDays(2),

        // A bill never ages out because missing one costs money, and a person's message
        // never ages out because that is the entire distinction being drawn.
        _ => null,
    };

    public static bool HasExpired(MailClass kind, DateTimeOffset arrivedUtc, DateTimeOffset nowUtc) =>
        Lifetime(kind) is { } lifetime && nowUtc - arrivedUtc >= lifetime;
}

/// <summary>
/// The owner's corrections, which outrank everything else.
///
/// Promoting a sender is <b>permanent</b>: it teaches Post for good rather than for one
/// message. Being wrong about the same sender twice is what would wear this feature out,
/// and a classifier the owner has to keep correcting is worse than no classifier.
/// </summary>
public sealed class SenderPromotions
{
    private readonly HashSet<string> promoted = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> All => promoted;

    public void Promote(string fromAddress) => promoted.Add(Normalize(fromAddress));

    public void Demote(string fromAddress) => promoted.Remove(Normalize(fromAddress));

    public bool IsPromoted(string fromAddress) => promoted.Contains(Normalize(fromAddress));

    /// <summary>The verdict, with the owner's correction applied over it.</summary>
    public MailClass? Apply(MailSignals signals, MailClass? verdict) =>
        IsPromoted(signals.FromAddress) ? MailClass.Human : verdict;

    // Senders do not write their own headers consistently, so the address is matched
    // without regard to case or surrounding space.
    private static string Normalize(string fromAddress) => fromAddress.Trim();

    public static SenderPromotions From(IEnumerable<string> addresses)
    {
        var promotions = new SenderPromotions();
        foreach (var address in addresses)
        {
            promotions.Promote(address);
        }
        return promotions;
    }
}
