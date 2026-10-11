using Pulp.Domain.Watching;

namespace Pulp.Domain.Sessions;

/// <summary>What a relayed session did, as far as Pulp cares.</summary>
public enum SessionEventKind
{
    /// <summary>Playing or paused: where it got to.</summary>
    Progress,

    /// <summary>The same app is now playing a different title (D288: a title ending or changing ends the locked hand-off).</summary>
    TitleChanged,

    /// <summary>The session closed short of the end (D30): seen, and not finished.</summary>
    StoppedShort,

    /// <summary>The session closed at or past the end fraction (D182).</summary>
    Ended,
}

/// <summary>
/// A relayed session, read: the kind of event, the title the app gave (if it did), and the
/// <see cref="SessionObservation"/> to record against the title or episode the household started.
/// </summary>
public sealed record SessionMeaning(SessionEventKind Kind, string Package, string? Title, SessionObservation Observation);

/// <summary>
/// Decides what a relayed session means and turns it into watch evidence (D30, D182, D286, D288).
///
/// <para>It never decides <i>watched</i>. It produces a <see cref="SessionObservation"/> and
/// <see cref="Watched.Decide"/> stays the one place that judges, so the ninety-five-percent rule
/// (<see cref="Watched.EndFraction"/>) is not restated here. The observation's <c>Ended</c> is set only
/// when the app closed the session at or past that fraction of a known duration; a session that
/// stopped early, or whose duration is unknown, is recorded as seen-but-short, never as ended. A stop
/// is not an end.</para>
/// </summary>
public static class SessionReading
{
    public static SessionMeaning Interpret(SessionMessage message, SessionMessage? previous, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(message);

        var reachedEnd = message.State == SessionPlayState.Stopped
            && message.Duration > TimeSpan.Zero
            && message.Position.Ticks >= message.Duration.Ticks * Watched.EndFraction;
        var observation = new SessionObservation(now, message.Position, message.Duration, reachedEnd);

        SessionEventKind kind;
        if (message.State == SessionPlayState.Stopped)
        {
            kind = reachedEnd ? SessionEventKind.Ended : SessionEventKind.StoppedShort;
        }
        else if (TitleChanged(message, previous))
        {
            kind = SessionEventKind.TitleChanged;
        }
        else
        {
            kind = SessionEventKind.Progress;
        }

        return new SessionMeaning(kind, message.Package, message.Title, observation);
    }

    private static bool TitleChanged(SessionMessage message, SessionMessage? previous) =>
        previous is not null
        && string.Equals(previous.Package, message.Package, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(previous.Title)
        && !string.IsNullOrWhiteSpace(message.Title)
        && !string.Equals(previous.Title!.Trim(), message.Title!.Trim(), StringComparison.OrdinalIgnoreCase);
}
