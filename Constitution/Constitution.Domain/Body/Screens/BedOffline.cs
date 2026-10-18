using Constitution.Domain.Credentials;
using Constitution.Domain.Notices;

namespace Constitution.Domain.Body.Screens;

/// <summary>
/// Whether the bed has gone quiet, and when it last spoke. <see cref="LastNight"/> is the evening the last
/// reading belongs to ("Thursday night"), since sleep is dated by the morning it ended.
/// </summary>
public sealed record BedOfflineState(bool IsOffline, DateOnly? LastReading, string? LastNight)
{
    /// <summary>The weekday the bed last reported, without "night": "Thursday".</summary>
    public string? LastWeekday => LastReading is { } day ? SignalWording.NightOf(day).ToString() : null;
}

/// <summary>
/// "Waiting for the bed" (C-7.2.2, book page 16). Nothing says the bed is offline: the owner is told so by
/// the sleep series stopping. With a side of the bed chosen and no sleep reading for two days, the sleep line
/// stops at the last reading and waits, the Body sentence says so calmly, and a Problem notice carries the
/// error, which is never voiced.
/// </summary>
public static class BedOffline
{
    /// <summary>Days with no sleep reading, counting back from today, before the bed is called quiet.</summary>
    public const int SilentDays = 2;

    /// <summary>The chart's strong line.</summary>
    public const string Headline = "Waiting for the bed";

    /// <summary>
    /// The state from the sleep series. A bed nobody has chosen is not judged, and a bed that never
    /// reported has not gone quiet, it has not started.
    /// </summary>
    public static BedOfflineState Judge(IEnumerable<BodyReading> readings, bool bedChosen, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var last = readings
            .Where(reading => reading.Signal == BodySignal.Sleep && reading.Day <= today)
            .Select(reading => (DateOnly?)reading.Day)
            .Max();
        if (last is null)
        {
            return new BedOfflineState(false, null, null);
        }

        var night = $"{SignalWording.NightOf(last.Value)} night";
        var offline = bedChosen && last.Value <= today.AddDays(-SilentDays);
        return new BedOfflineState(offline, last, night);
    }

    /// <summary>The calm fact the Body sentence is told, and the plain sentence shown without a voice.</summary>
    public static string Sentence(BedOfflineState state)
    {
        if (!state.IsOffline || state.LastWeekday is null)
        {
            throw new ArgumentException("The bed has not gone quiet, so there is nothing to say about it.", nameof(state));
        }

        return $"The bed hasn’t reported since {state.LastWeekday}, so that line waits.";
    }

    /// <summary>
    /// States the bed to the board: quiet raises the Problem notice, under the same cause the Settings credential check
    /// uses so the two never stack; reporting again takes it away, unless the login is refused, which is Settings' to clear.
    /// Returns whether the board changed.
    /// </summary>
    public static bool Apply(NoticeBoard board, BedOfflineState state, bool credentialRejected, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(state);
        var cause = ConnectionNotices.CauseKey(CredentialIds.SleepNumber);
        if (state.IsOffline)
        {
            return board.Raise(NoticeKind.BedOffline, cause, now);
        }

        return !credentialRejected && board.Resolve(cause);
    }
}
