using Codex.Domain.Post;

namespace Codex.Domain.Plate;

/// <summary>
/// The inbound half of the handoff: another app asks, Plate looks, Plate returns.
///
/// <para>The design walkthrough moved <b>reading</b> from the outbound side to this one.
/// A statement read starts in Index; a card read starts in Post. Plate never works out what
/// a piece of paper is for — it is asked, it reads, it returns — which is the simpler
/// contract, because the app that wants the data already knows what it wants.</para>
///
/// <para>Two actions rather than one with a "kind" extra: a caller that sends the wrong
/// kind gets an activity-not-found it can see, instead of a result it cannot use.</para>
///
/// <para><b>Index does not exist yet</b> — it is P11 — so Plate's end of the statement read
/// ships with no live counterpart, and the on-device test plan has to say so rather than
/// imply a passing check.</para>
/// </summary>
public static class ReadRequest
{
    public const string ReadCard = "com.storyworthtelling.plate.action.READ_CARD";
    public const string ReadStatement = "com.storyworthtelling.plate.action.READ_STATEMENT";

    public const string NameExtra = "name";
    public const string PhoneExtra = "phone";
    public const string EmailExtra = "email";

    /// <summary>
    /// A card as plain strings. An empty field is left out rather than sent blank: a blank
    /// extra and a missing one mean different things to the caller — one is "the card says
    /// nothing here", the other is "I filled this in with nothing".
    /// </summary>
    public static IReadOnlyDictionary<string, string> CardExtras(CardReading reading)
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(extras, NameExtra, reading.Name);
        Add(extras, PhoneExtra, reading.Phone);
        Add(extras, EmailExtra, reading.Email);
        return extras;
    }

    /// <summary>
    /// A statement, on <b>the contract Post already uses</b>. Plate becomes a second sender
    /// of the same payload rather than a second dialect of it — Index must not have to know
    /// which app spoke. Null when there is no balance: a fabricated one would reach Index
    /// and be believed, which is far worse than one the owner has to read themselves.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? StatementExtras(MoneyFacts facts) =>
        IndexHandoff.PayloadFor(facts) is { } payload ? IndexHandoff.Extras(payload) : null;

    private static void Add(Dictionary<string, string> extras, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            extras[key] = value;
        }
    }
}
