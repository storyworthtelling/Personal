using Constitution.Domain.Health;
using Constitution.Domain.Notices;

namespace Constitution.Domain.Body.Screens;

/// <summary>Where a signal stands with Health Connect when its chart is opened.</summary>
public enum AskKind
{
    /// <summary>Allowed (or worked out from the pages): nothing to ask.</summary>
    None,

    /// <summary>Never asked: the chart says what it needs, once.</summary>
    FirstOpen,

    /// <summary>Asked and not allowed: the chart waits and the bar still offers it.</summary>
    Declined,

    /// <summary>Allowed once and taken away since: a Problem notice, and the chart waits.</summary>
    Revoked,
}

/// <summary>
/// What a chart says when Health Connect has not let it read (C-7.3.5, book page 16): what it needs,
/// that it only reads, and the one bar action. Health Connect is asked per signal, the first time its
/// chart is opened, never all at once up front.
/// </summary>
public sealed record PermissionAsk(AskKind Kind, string Need, string? Note, string? BarLabel)
{
    /// <summary>The pinned bar's action, in the book's words.</summary>
    public const string AllowLabel = "Allow in Health Connect";

    /// <summary>The cause the Problem notice is raised under: one for all, so two signals are one card.</summary>
    public const string RevokedCauseKey = "health-connect:revoked";

    private const string FirstNote = "Asked now because this is the first time the chart was opened.";
    private const string DeclinedNote = "Not allowed yet. Allow it here whenever you like.";
    private const string RevokedNote = "Allow it again and the chart picks up where it stopped.";

    /// <summary>
    /// The ask for one signal. <paramref name="revokedNoticeWaiting"/> keeps a revoked chart waiting after the
    /// open-time reconcile has already demoted the saved choice from allowed to asked.
    /// </summary>
    public static PermissionAsk For(BodySignal signal, SignalChoice choice, bool grantedNow, bool revokedNoticeWaiting = false)
    {
        if (!BodySignals.HealthConnect.Contains(signal) || grantedNow)
        {
            return new PermissionAsk(AskKind.None, string.Empty, null, null);
        }

        var name = BodySignals.Name(signal).ToLowerInvariant();
        if (choice == SignalChoice.Granted || revokedNoticeWaiting)
        {
            return new PermissionAsk(
                AskKind.Revoked,
                $"Health Connect no longer lets Constitution read {name}, so this chart waits.",
                RevokedNote,
                AllowLabel);
        }

        var need = $"Constitution needs to read {name} from Health Connect. It reads only; it never writes.";
        return choice == SignalChoice.NotAsked
            ? new PermissionAsk(AskKind.FirstOpen, need, FirstNote, AllowLabel)
            : new PermissionAsk(AskKind.Declined, need, DeclinedNote, AllowLabel);
    }

    /// <summary>The signals that were allowed and no longer are, read from the saved choices before they are reconciled.</summary>
    public static IReadOnlyList<BodySignal> RevokedSignals(HealthChoices choices, Func<BodySignal, bool> isGranted)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(isGranted);
        return BodySignals.HealthConnect
            .Where(signal => choices.Of(signal) == SignalChoice.Granted && !isGranted(signal))
            .ToList();
    }

    /// <summary>
    /// States the revoked signals to the board: any raises the one Problem notice, none takes it away. The
    /// cause names no signal and no value. Returns whether the board changed.
    /// </summary>
    public static bool Apply(NoticeBoard board, IReadOnlyCollection<BodySignal> revoked, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(revoked);
        return revoked.Count > 0
            ? board.Raise(NoticeKind.HealthConnectRevoked, RevokedCauseKey, now)
            : board.Resolve(RevokedCauseKey);
    }
}
