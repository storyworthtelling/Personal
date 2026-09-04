namespace Codex.Domain.Audio;

/// <summary>Which side of the app a download came from. Only for reading the meter —
/// the budget itself is one pool.</summary>
public enum DownloadKind
{
    Music,
    Podcast,
}

/// <summary>
/// One thing held on the device, and when it was taken.
/// </summary>
/// <param name="Title">
/// What it is called, kept here rather than looked up. A download is a thing the
/// owner chose, and it has to stay nameable on the downloads screen even after the
/// file it came from has left Drive or the show has been unsubscribed — which is
/// exactly when they are most likely to be looking for it.
/// </param>
/// <param name="DownloadedUtc">
/// When it was downloaded, and never touched again. The sweep runs from this and
/// <b>not</b> from when it was last played: the question is how long something has
/// been taking up room, not how recently it was useful. A clock that reset on play is
/// how a twelve-hour budget quietly becomes a library.
/// </param>
public sealed record DownloadedItem(
    string MediaId, string Title, DownloadKind Kind, TimeSpan Duration, DateTimeOffset DownloadedUtc);

/// <summary>
/// What is downloaded, measured in <b>playback time</b> rather than megabytes.
///
/// That is the owner's unit and it is the honest one: twelve hours is a thing you can
/// picture — a flight, a week of commutes — where 4.7 GB is a number you have to
/// convert before it means anything, and which means something different for a
/// podcast than for a lossless album.
///
/// One pool across music and podcasts. The meter splits it so you can see where it
/// went, but nothing is reserved for either side; the owner decides what a week is
/// for, not the app.
///
/// Pure and immutable — every change returns a new budget — so the store writes a
/// value it was handed rather than mutating something the screen is also reading.
/// </summary>
public sealed record DownloadBudget(IReadOnlyList<DownloadedItem> Items)
{
    /// <summary>Twelve hours of playback, shared. The owner's number, and the reason
    /// is theirs: not getting into the habit of filling the phone.</summary>
    public static readonly TimeSpan Capacity = TimeSpan.FromHours(12);

    /// <summary>How long a download is kept before the sweep takes it.</summary>
    public const int LifeDays = 10;

    public static DownloadBudget Empty { get; } = new([]);

    public TimeSpan Used => Total(Items);

    public TimeSpan UsedBy(DownloadKind kind) => Total(Items.Where(item => item.Kind == kind));

    /// <summary>What is left, floored at zero — a budget the owner chose to go past
    /// reports empty rather than negative.</summary>
    public TimeSpan Remaining
    {
        get
        {
            var left = Capacity - Used;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public bool Holds(string mediaId) => Items.Any(item => item.MediaId == mediaId);

    public bool Fits(TimeSpan duration) => Used + duration <= Capacity;

    /// <summary>
    /// How far past the budget adding this would put things, or zero if it fits.
    /// Over budget asks each time, so the question has to name the cost — and it
    /// keeps counting once the budget is already past rather than starting again.
    /// </summary>
    public TimeSpan Overage(TimeSpan duration)
    {
        var over = Used + duration - Capacity;
        return over > TimeSpan.Zero ? over : TimeSpan.Zero;
    }

    /// <summary>Adds a download. Holding the same thing twice is not a thing, so a
    /// repeat replaces rather than doubles.</summary>
    public DownloadBudget With(DownloadedItem item) =>
        new([.. Items.Where(held => held.MediaId != item.MediaId), item]);

    public DownloadBudget Without(string mediaId) =>
        new([.. Items.Where(item => item.MediaId != mediaId)]);

    /// <summary>What the sweep would take.</summary>
    public IReadOnlyList<DownloadedItem> Expired(DateTimeOffset now) =>
        [.. Items.Where(item => now - item.DownloadedUtc >= TimeSpan.FromDays(LifeDays))];

    /// <summary>The budget with everything expired removed.</summary>
    public DownloadBudget Swept(DateTimeOffset now)
    {
        var going = Expired(now).Select(item => item.MediaId).ToHashSet(StringComparer.Ordinal);
        return going.Count == 0 ? this : new DownloadBudget([.. Items.Where(item => !going.Contains(item.MediaId))]);
    }

    /// <summary>How many days something has left, or null if it is not held. Shown
    /// beside a download so the sweep is never a surprise.</summary>
    public int? DaysLeft(string mediaId, DateTimeOffset now)
    {
        var item = Items.FirstOrDefault(held => held.MediaId == mediaId);
        if (item is null)
        {
            return null;
        }

        var left = LifeDays - (now - item.DownloadedUtc).TotalDays;
        return Math.Max(0, (int)Math.Ceiling(left));
    }

    private static TimeSpan Total(IEnumerable<DownloadedItem> items) =>
        items.Aggregate(TimeSpan.Zero, (sum, item) => sum + item.Duration);
}
