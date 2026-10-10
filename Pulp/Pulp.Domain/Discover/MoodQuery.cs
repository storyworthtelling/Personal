using Pulp.Domain.Core;

namespace Pulp.Domain.Discover;

/// <summary>A mood search as it is sent (D171): the words typed under the Mood chip and the household's context.</summary>
public sealed record MoodQuery
{
    /// <summary>Results show the top twenty (D172).</summary>
    public const int DefaultMaxTitles = 20;

    /// <summary>A mood is a phrase, not a document; anything longer is cut rather than sent whole.</summary>
    public const int MaxMoodLength = 200;

    private MoodQuery(string mood, DiscoverContext context, int maxTitles)
    {
        Mood = mood;
        Context = context;
        MaxTitles = maxTitles;
    }

    public string Mood { get; }

    public DiscoverContext Context { get; }

    public int MaxTitles { get; }

    public static MoodQuery For(string mood, DiscoverContext context, int maxTitles = DefaultMaxTitles)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTitles);
        return new MoodQuery(DiscoverText.Phrase(mood, MaxMoodLength, nameof(mood)), context, maxTitles);
    }
}

/// <summary>One title the model named, by name and — when it gave them — year and kind. Not yet a real title.</summary>
public sealed record MoodCandidate(string Name, int? Year, TitleKind? Kind);

/// <summary>How the ask went. Anything but <see cref="Answered"/> is "no idea" and is never voiced (D99, G12).</summary>
public enum MoodOutcome
{
    Answered,

    /// <summary>No Claude key is set, so nothing was asked.</summary>
    NoKey,

    /// <summary>The key was refused; credential health has been told.</summary>
    Rejected,

    /// <summary>The service could not be reached or was in trouble; not the key's fault.</summary>
    Unavailable,

    /// <summary>A reply came back that was not the shape asked for.</summary>
    Malformed,
}

/// <summary>
/// The model's answer, before anything is checked: a suggested name (for a playlist) and the titles
/// it named, in its order. <see cref="Detail"/> says what went wrong for the diagnostics log only.
/// </summary>
public sealed record MoodAnswer
{
    private MoodAnswer(MoodOutcome outcome, string title, IReadOnlyList<MoodCandidate> candidates, string detail)
    {
        Outcome = outcome;
        Title = title;
        Candidates = candidates;
        Detail = detail;
    }

    public MoodOutcome Outcome { get; }

    public string Title { get; }

    public IReadOnlyList<MoodCandidate> Candidates { get; }

    public string Detail { get; }

    public static MoodAnswer Answered(string? title, IReadOnlyList<MoodCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return new MoodAnswer(MoodOutcome.Answered, title?.Trim() ?? string.Empty, candidates.ToList(), string.Empty);
    }

    public static MoodAnswer NoIdea(MoodOutcome why, string detail = "")
    {
        if (why == MoodOutcome.Answered)
        {
            throw new ArgumentException("\"No idea\" needs a reason other than an answer.", nameof(why));
        }

        return new MoodAnswer(why, string.Empty, [], detail);
    }
}

internal static class DiscoverText
{
    public static string Phrase(string? text, int maxLength, string what)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("There is nothing to ask about; a blank " + what + " is not sent.", what);
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }
}
