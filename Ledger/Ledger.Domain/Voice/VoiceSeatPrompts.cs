using System.Text.RegularExpressions;

namespace Index.Domain.Voice;

/// <summary>
/// Every occasion Index is willing to speak in the owner's voice — a closed list, as Folio's is, and for
/// the same reason: a voice is for the app's own remarks, never its bad news. These are the
/// <c>Purpose</c> of a model request, which the shared messenger may log, so they describe the occasion and
/// carry no data at all.
/// </summary>
public static class VoicedOccasions
{
    public const string Accounts = "a line above the accounts about how the household's debt has been moving";

    public const string Snowball = "a line above the debt-free date about how far the debt has come down";

    public const string Savings = "a line under the switch on the savings page about the pace the savings are growing at";

    public static IReadOnlyList<string> All { get; } = [Accounts, Snowball, Savings];

    public static string For(VoiceSeat seat) => seat switch
    {
        VoiceSeat.Accounts => Accounts,
        VoiceSeat.Snowball => Snowball,
        VoiceSeat.Savings => Savings,
        _ => throw new ArgumentOutOfRangeException(nameof(seat), seat, "That seat has no occasion."),
    };
}

/// <summary>
/// The only free text a seat's model call is given besides the facts, pinned in one place the way Foreword's
/// <c>HandwritingInstruction</c> is so that a change to it is a decision and not a drift. Words, never
/// digits: a number in an instruction is a number the model was handed without a fact to stand on, and
/// <c>VoiceSeatPromptTests</c> fails on one.
///
/// <para>Accounts and Snowball speak in percentages and counts (D114) and are told so. Savings is the one
/// seat that may speak an amount, and only as a rate per month (mockup 9: "Three hundred a month, month
/// after month."); it is told to mention nothing else.</para>
/// </summary>
public static class VoiceSeatInstructions
{
    private const string Common =
        "Write one short sentence in the owner's voice, as a line of copy at the top of a screen in a " +
        "personal finance notebook. Build it ONLY from the FACTS below: use no number the FACTS do not " +
        "give, and do not invent one. Say numbers in words, and percentages in words as in \"about one " +
        "percent\". No advice, no instruction, no judgement, no exclamation marks, no emoji, no quotation " +
        "marks. At most eighteen words. If the FACTS are not enough for a sentence, reply with nothing at all.";

    public const string Accounts =
        Common + " Speak only in percentages and counts. Never state a dollar amount, an account number, " +
        "a bank name or an account name.";

    public const string Snowball =
        Common + " Speak only in percentages and counts. Never state a dollar amount, an account number, " +
        "a bank name or an account name.";

    public const string Savings =
        Common + " Mention only the monthly rate the FACTS give, as an amount per month. Never state a " +
        "balance, a total saved, a goal or target, a date, an account name or a bank name.";

    public static string For(VoiceSeat seat) => seat switch
    {
        VoiceSeat.Accounts => Accounts,
        VoiceSeat.Snowball => Snowball,
        VoiceSeat.Savings => Savings,
        _ => throw new ArgumentOutOfRangeException(nameof(seat), seat, "That seat has no instruction."),
    };
}

/// <summary>What the composer sends: the occasion (<c>Purpose</c>), the pinned instruction followed by the
/// facts (<c>Context</c>), and the word limit. Plain strings so the Domain needs no reference to the voice
/// library; <c>VoiceSeatComposer</c> wraps it in the library's request.</summary>
public sealed record VoiceSeatPrompt(string Occasion, string Context, int MaxWords);

public static class VoiceSeatPrompts
{
    /// <summary>The request for a seat, or null when there are no facts to write it from — the seat is
    /// then hidden and no model call is made.</summary>
    public static VoiceSeatPrompt? Build(VoiceFacts facts)
    {
        if (!facts.HasFacts)
        {
            return null;
        }

        var context =
            VoiceSeatInstructions.For(facts.Seat)
            + "\n\nFACTS (the only things you may draw on):\n"
            + string.Join('\n', facts.Lines.Select(line => $"- {line}"));
        return new VoiceSeatPrompt(VoicedOccasions.For(facts.Seat), context, MaxWords: 24);
    }
}

/// <summary>
/// The sentence the model sends back, checked before anyone sees it. The instruction says what to leave
/// out; this is the second lock, because an instruction is a request and a reply is the evidence. A reply
/// that is blank, runs on, or — on the seats that speak no amounts — states one is set aside and the seat
/// stays hidden.
/// </summary>
public static class VoiceReplyCheck
{
    public const int MaxWords = 30;

    private static readonly Regex StatesAnAmount = new(
        @"\$|\d{3,}|\d,\d{3}|\b(dollars?|usd|bucks)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The trimmed sentence, or null when it should not be shown.</summary>
    public static string? Accept(VoiceSeat seat, string? reply)
    {
        var sentence = reply?.Trim();
        if (string.IsNullOrEmpty(sentence)
            || sentence.Contains('\n')
            || sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxWords)
        {
            return null;
        }

        return seat != VoiceSeat.Savings && StatesAnAmount.IsMatch(sentence) ? null : sentence;
    }
}
