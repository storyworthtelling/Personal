using Codex.Domain.Policy;
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
/// <para>Both reads are answered by <see cref="PaperActivity"/> and only for a caller holding
/// <see cref="PaperPermission"/>; Index starts the statement read from a hand-entered account,
/// and Post starts the card read from the Rolodex (audit W2).</para>
/// </summary>
public static class ReadRequest
{
    public const string ReadCard = "com.storyworthtelling.plate.action.READ_CARD";
    public const string ReadStatement = "com.storyworthtelling.plate.action.READ_STATEMENT";

    /// <summary>
    /// The component that answers <see cref="ReadCard"/> and <see cref="ReadStatement"/>. Its own activity, behind
    /// <see cref="PaperPermission"/>, because the camera filters on the errand activity must stay open to every app, a
    /// permission cannot be set per filter, and a statement read hands back a balance.
    /// </summary>
    public const string PaperActivity = PolicyContext.PlatePackage + ".PaperReadActivity";

    /// <summary>The signature-level permission a card or statement caller must hold.</summary>
    public const string PaperPermission = PolicyContext.PlatePackage + ".READ_PAPER";

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
    ///
    /// Sender name is passed as <c>null</c> deliberately: statement OCR has no
    /// institution-name extraction built yet (per this file's own doc comment, this path has
    /// never been exercised against a real device). A future release that builds OCR'd
    /// sender-name extraction from a scanned statement's header can populate this without
    /// another contract change — the field already exists on the wire.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? StatementExtras(MoneyFacts facts) =>
        IndexHandoff.PayloadFor(facts, senderName: null) is { } payload ? IndexHandoff.Extras(payload) : null;

    private static void Add(Dictionary<string, string> extras, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            extras[key] = value;
        }
    }
}
