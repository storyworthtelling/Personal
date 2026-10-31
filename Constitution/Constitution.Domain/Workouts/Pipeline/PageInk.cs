namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>What a look at a page on the tablet found: ink the owner put there, none, or no way to say.</summary>
public enum PageInk
{
    /// <summary>The page was read through and carries no pen ink, and the cloud's copy is known to carry pen ink when there is some.</summary>
    Blank,

    /// <summary>There is ink on the page: the owner has started on it.</summary>
    HasInk,

    /// <summary>It could not be said: the page could not be fetched or read, or the cloud's copy is not yet known to carry ink at all.</summary>
    Unknown,
}

/// <summary>What the probe said, and the failure behind an <see cref="PageInk.Unknown"/> when there was one (kept with its stack, never its message in a trail).</summary>
public sealed record InkProbe(PageInk Ink, Exception? Failure = null)
{
    public static InkProbe Blank { get; } = new(PageInk.Blank);

    public static InkProbe HasInk { get; } = new(PageInk.HasInk);

    public static InkProbe Unknown(Exception? failure = null) => new(PageInk.Unknown, failure);
}

/// <summary>
/// Looks at the page the tablet holds under a title and says whether the owner has put ink on it (S3). A page is never taken off
/// the tablet, and never replaced by a blank one, without asking: the read-back reads a blank page as a skip, so a blank copy set down
/// over a page the owner had begun writing on is a lost session and a recorded skip they never made.
///
/// <para>Only <see cref="PageInk.Blank"/> allows the page to go. <see cref="PageInk.HasInk"/> and <see cref="PageInk.Unknown"/> keep
/// it; an implementation that cannot be sure answers Unknown, never Blank.</para>
/// </summary>
public interface IPageInkProbe
{
    Task<InkProbe> ProbeAsync(string title, CancellationToken cancellation);
}
