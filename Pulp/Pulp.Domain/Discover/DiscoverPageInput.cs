namespace Pulp.Domain.Discover;

/// <summary>The chip beside Discover's search field (D171): what the typed words are.</summary>
public enum DiscoverSearchMode
{
    Title,
    Mood,
}

/// <summary>What the page does with the field as it stands.</summary>
public enum DiscoverPageAction
{
    /// <summary>The field is empty: the stack shows (D170).</summary>
    ShowStack,

    /// <summary>Keep what is showing; ask again at <see cref="DiscoverPageStep.AskAgainAt"/> if one is given.</summary>
    Wait,

    /// <summary>Search the catalogue by title (D209).</summary>
    SearchTitles,

    /// <summary>A mood is typed but not yet sent: the page says how to send it.</summary>
    AwaitMoodDone,

    /// <summary>Ask the model for titles that fit the mood (D171).</summary>
    AskMood,
}

/// <summary>One decision about the field. <see cref="Text"/> is what is searched or sent, trimmed and cut to length.</summary>
public sealed record DiscoverPageStep(DiscoverPageAction Action, string Text, DateTimeOffset? AskAgainAt);

/// <summary>
/// The search field under Discover's header (D170, D171, D209), decided with the time passed in so the
/// debounce is a rule rather than a timer. Empty shows the stack; typing replaces it in place. A title is
/// searched once the typing pauses, or at once on Done. A mood is <b>explicit</b>: it is asked only on
/// Done (or a tap on the Mood chip), never on a pause, because every ask is a model call and half a
/// phrase is not a mood.
/// </summary>
public static class DiscoverPageInput
{
    /// <summary>How long the typing must rest before a title search goes out.</summary>
    public static readonly TimeSpan TitlePause = TimeSpan.FromMilliseconds(400);

    /// <summary>A single letter matches half the catalogue; it is searched only when asked for with Done.</summary>
    public const int MinTitleLength = 2;

    /// <summary>The field's hint, as mockup 03 draws it (team search arrives with Sports, D209).</summary>
    public const string Hint = "Search titles and teams";

    /// <summary>The chips in their drawn order.</summary>
    public static IReadOnlyList<DiscoverSearchMode> Modes { get; } = [DiscoverSearchMode.Title, DiscoverSearchMode.Mood];

    public static string Label(DiscoverSearchMode mode) => mode switch
    {
        DiscoverSearchMode.Title => "Title",
        DiscoverSearchMode.Mood => "Mood",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "A search mode with no chip."),
    };

    /// <param name="editedAt">When the field last changed.</param>
    /// <param name="submitted">Done was pressed, or the chip was tapped with words in the field.</param>
    public static DiscoverPageStep Decide(string? text, DiscoverSearchMode mode, DateTimeOffset editedAt, DateTimeOffset now, bool submitted)
    {
        var words = text?.Trim() ?? string.Empty;
        if (words.Length == 0)
        {
            return new DiscoverPageStep(DiscoverPageAction.ShowStack, string.Empty, null);
        }

        if (mode == DiscoverSearchMode.Mood)
        {
            var mood = words.Length <= MoodQuery.MaxMoodLength ? words : words[..MoodQuery.MaxMoodLength].TrimEnd();
            return submitted
                ? new DiscoverPageStep(DiscoverPageAction.AskMood, mood, null)
                : new DiscoverPageStep(DiscoverPageAction.AwaitMoodDone, mood, null);
        }

        if (mode != DiscoverSearchMode.Title)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "A search mode the field does not know.");
        }

        if (submitted)
        {
            return new DiscoverPageStep(DiscoverPageAction.SearchTitles, words, null);
        }

        if (words.Length < MinTitleLength)
        {
            return new DiscoverPageStep(DiscoverPageAction.Wait, words, null);
        }

        var due = editedAt + TitlePause;
        return now >= due
            ? new DiscoverPageStep(DiscoverPageAction.SearchTitles, words, null)
            : new DiscoverPageStep(DiscoverPageAction.Wait, words, due);
    }
}
