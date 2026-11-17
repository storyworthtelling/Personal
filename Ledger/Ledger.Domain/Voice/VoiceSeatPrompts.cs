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
    public static string Accounts => Line(VoiceSeat.Accounts).Purpose;

    public static string Snowball => Line(VoiceSeat.Snowball).Purpose;

    public static string Savings => Line(VoiceSeat.Savings).Purpose;

    public static IReadOnlyList<string> All { get; } = [Accounts, Snowball, Savings];

    public static string For(VoiceSeat seat) => Line(seat).Purpose;

    /// <summary>The seat's entry on the suite's voice-line Call Sheet (audit W10.3): purpose, instruction, words.</summary>
    public static Storyworthtelling.CallSheet.VoiceLine Line(VoiceSeat seat) => seat switch
    {
        VoiceSeat.Accounts => Storyworthtelling.CallSheet.CallSheet.For("Index", "accounts"),
        VoiceSeat.Snowball => Storyworthtelling.CallSheet.CallSheet.For("Index", "snowball"),
        VoiceSeat.Savings => Storyworthtelling.CallSheet.CallSheet.For("Index", "savings"),
        _ => throw new ArgumentOutOfRangeException(nameof(seat), seat, "That seat has no occasion."),
    };
}

/// <summary>
/// The only free text a seat's model call is given besides the facts, written on the suite's voice-line Call
/// Sheet (audit W10.3) so that a change to it is a decision and not a drift. Words, never
/// digits: a number in an instruction is a number the model was handed without a fact to stand on, and
/// <c>VoiceSeatPromptTests</c> fails on one.
///
/// <para>Accounts and Snowball speak in percentages and counts (D114) and are told so. Savings is the one
/// seat that may speak an amount, and only as a rate per month (mockup 9: "Three hundred a month, month
/// after month."); it is told to mention nothing else.</para>
/// </summary>
public static class VoiceSeatInstructions
{
    public static string Accounts => For(VoiceSeat.Accounts);

    public static string Snowball => For(VoiceSeat.Snowball);

    public static string Savings => For(VoiceSeat.Savings);

    /// <summary>The seat's standing instruction, written on the suite's voice-line Call Sheet.</summary>
    public static string For(VoiceSeat seat) => VoicedOccasions.Line(seat).Guidance
        ?? throw new InvalidOperationException($"The Call Sheet gives Index’s {seat} seat no instruction.");
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
        return new VoiceSeatPrompt(VoicedOccasions.For(facts.Seat), context, VoicedOccasions.Line(facts.Seat).MaxWords);
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
