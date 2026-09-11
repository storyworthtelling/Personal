using Foreword.Domain.Brief;

namespace Foreword.Domain.Interfaces;

/// <summary>
/// What the owner has set on the Sources screen, and the marks the brief keeps between mornings.
///
/// <para>A seam rather than a settings type, because the delivery runs in Infrastructure and the
/// values live in the phone's preferences — and because the comic's high-water mark has to be
/// written back at the end of a morning, which makes this read <i>and</i> write.</para>
/// </summary>
public interface IBriefPreferences
{
    int NewsCount { get; }
    int OpinionCount { get; }

    IReadOnlyList<Feed> Feeds { get; }

    /// <summary>The highest xkcd already printed; 0 on a phone that has never had one.</summary>
    int SeenComic { get; set; }

    ReadingTaste Taste { get; }

    /// <summary>Records what a morning's fetching found — marking failures, clearing recoveries.</summary>
    void FeedsAfterAMorning(IReadOnlyCollection<string> failed, DateOnly today);
}
