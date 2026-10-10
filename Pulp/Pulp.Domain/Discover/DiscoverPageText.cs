namespace Pulp.Domain.Discover;

/// <summary>
/// Discover's quiet status sentences (DESIGN.md §5: loading is a sentence, never a spinner). None is
/// voiced: Discover has no voiced seat but the used-up stack's empty state (D112), and "no idea" from
/// the model is one plain sentence whatever the reason — the reason goes to the diagnostics log (D99, G12).
/// </summary>
public static class DiscoverPageText
{
    /// <summary>Above the piles, as mockup 03 draws it: the stack turns over at Friday 6 am (D110).</summary>
    public const string StackStatus = "This week's stack · until Friday";

    public const string SwipeHint = "Swipe for the next";

    public const string BringItBack = "Bring it back";

    public const string FilmsHeading = "Films";

    public const string ShowsHeading = "Shows";

    public const string Dealing = "Dealing this week's stack…";

    /// <summary>No roster yet: the household is set up at first run (Release D).</summary>
    public const string NoHousehold = "The stack is dealt once the household is set up.";

    /// <summary>A roster with two adults and nothing saying whose phone this is.</summary>
    public const string WhosePhone = "The stack is dealt once this phone knows whose it is.";

    /// <summary>A week with nothing in it: no titles on the household's services came back to deal from.</summary>
    public const string NothingToDeal = "Nothing to deal this week yet.";

    /// <summary>The household has named no services yet, so there is nothing to deal from (Settings, Release D).</summary>
    public const string NoServices = "The stack is dealt from your services, once Settings names them.";

    public const string StackUnreachable = "This week's stack could not be dealt just now.";

    public const string SearchUnreachable = "The catalogue could not be reached just now.";

    public const string MoodPrompt = "Press Done to ask for this mood.";

    public const string NothingForMood = "Nothing on your services fits that mood.";

    public const string NoTrailer = "No trailer found for this one.";

    private const string NoIdea = "No ideas for that mood just now.";

    public static string Searching(string text) => $"Looking for “{text.Trim()}”…";

    public static string Asking(string mood) => $"Asking for “{mood.Trim()}”…";

    public static string NoTitles(string text) => $"Nothing called “{text.Trim()}”.";

    /// <summary>
    /// The one sentence for every way the model can have no idea — no key, a refused key, an
    /// unreachable service, a garbled reply. The page never tells them apart; the log does.
    /// </summary>
    public static string MoodNoIdea(MoodOutcome outcome)
    {
        if (outcome == MoodOutcome.Answered)
        {
            throw new ArgumentException("An answer is not \"no idea\"; resolve it instead.", nameof(outcome));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A mood outcome the page does not know.");
        }

        return NoIdea;
    }
}
