namespace Folio.Domain.Voice;

/// <summary>
/// Every occasion Folio is willing to speak in the owner's voice — a closed list, so a new voiced
/// surface is a decision rather than a diff.
///
/// <para><b>Errors are not among them, and that is the rule.</b> A voice is for the app's own
/// remarks, not its bad news. A failed review, a rejected key, a Drive that will not answer: those
/// are facts the owner has to act on, and dressing them in their own wit makes them read as banter
/// about their book rather than as something wrong. It would also put a model call on the path of
/// telling someone a model call failed, which is the one moment it is least likely to work.</para>
///
/// <para>It is an appealing mistake, because a failure message is exactly the copy that feels most
/// improved by sounding human — which is why the list is enforced rather than remembered.</para>
/// </summary>
public static class VoicedOccasions
{
    /// <summary>The line at the top of the desk when the app opens.</summary>
    public const string Welcome = "welcome";

    /// <summary>A book's own line on the desk, about how far along it is.</summary>
    public const string DeskLine = "a one-line note on the desk about how this book is coming along";

    /// <summary>
    /// A desk with no manuscripts on it. Not a failure — it is the ordinary first state of a new
    /// install, and the one place in the app with room for the owner to be spoken to at all.
    /// </summary>
    public const string EmptyDesk = "a line for an empty desk, inviting a first manuscript";

    public static IReadOnlyList<string> All { get; } = [Welcome, DeskLine, EmptyDesk];
}
