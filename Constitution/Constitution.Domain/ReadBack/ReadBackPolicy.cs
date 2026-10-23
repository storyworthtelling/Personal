using Constitution.Domain.Delivery;
using Constitution.Domain.Ink;

namespace Constitution.Domain.ReadBack;

/// <summary>When a page is trashed and when a document is let go. Days are whole calendar days on the owner's own clock.</summary>
public static class ReadBackPolicy
{
    /// <summary>The digest is trashed after three days, since the cloud's "opened" signal is not there to use (R4).</summary>
    public const int DigestDays = 3;

    /// <summary>A document that has not settled in a week is left on the tablet and not tried again.</summary>
    public const int GiveUpAfterDays = 7;

    /// <summary>
    /// Whether the read-back may look at a document today. A workout waits for its day to end: its page is blank until the
    /// session has happened, and a blank is read as a skip once pen ink is proven. A meals sheet is read through its own
    /// day (the deadline, not the date, decides what its silence means) and a digest is only ever trashed, by its own age.
    /// </summary>
    public static bool IsDue(DeliveryKind kind, DateOnly day, DateOnly today) =>
        kind != DeliveryKind.Workout || day < today;

    public static bool DigestIsDue(DateOnly day, DateOnly today) => today.DayNumber - day.DayNumber >= DigestDays;

    public static bool HasGivenUp(DateOnly day, DateOnly today) => today.DayNumber - day.DayNumber > GiveUpAfterDays;

    /// <summary>
    /// Null when every page can go; otherwise why one has to stay. A box that was not read keeps the document, and so
    /// does silence that is not yet an answer (a meal before its deadline). A question does not: it is saved with the
    /// result and waits on the Waiting page, and the resolver's own "undecided" beside it (a Done box waiting on the
    /// rating's question) is that same question, not a reason to read the page again.
    /// </summary>
    public static string? WhyKept(IReadOnlyList<PageResolutions> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
        {
            return "There was nothing read to save.";
        }

        foreach (var page in pages)
        {
            if (page.Resolutions.Any(resolution => resolution is InkResolution.NotRead))
            {
                return $"A box on page {page.Number} was not read.";
            }

            var asking = page.Resolutions.Any(resolution => resolution is InkResolution.NeedsAsking);
            if (!asking && page.Resolutions.Any(resolution => resolution is InkResolution.Undecided))
            {
                return $"A box on page {page.Number} is still waiting for its deadline.";
            }
        }

        return null;
    }
}
