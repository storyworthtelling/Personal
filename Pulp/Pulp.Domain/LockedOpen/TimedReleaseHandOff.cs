namespace Pulp.Domain.LockedOpen;

/// <summary>
/// What Pulp asks Flyleaf before it opens a streaming title: lift the Entertainment block on that one service
/// for the length of the title, so it can play through to its end. The wire names are Flyleaf's own, copied
/// from <c>FocusPhone/Codex.Domain/Apps/TimedRelease.cs</c> (<c>TimedReleaseHandoff</c>); the guard in
/// <c>TimedReleaseContractTests</c> reads that file by content, so a drift on either side fails there.
///
/// An explicit ordered broadcast to Flyleaf's receiver under the signature permission the locked open already
/// uses. Pulp sends it only for a title the planner has allowed, and only for a service it is about to open.
/// </summary>
public static class TimedReleaseHandOff
{
    public const string Package = "com.storyworthtelling.focusphone.launcher";

    public const string Receiver = "com.storyworthtelling.focusphone.launcher.TimedReleaseReceiver";

    public const string Action = "com.storyworthtelling.focusphone.launcher.action.TIMED_RELEASE";

    public const string Permission = "com.storyworthtelling.focusphone.launcher.RECEIVE_LOCKED_OPEN";

    public const string PackageExtra = "package";

    public const string LinkExtra = "link";

    public const string MinutesExtra = "minutes";

    /// <summary>Flyleaf set the release: <c>Activity.RESULT_OK</c>.</summary>
    public const int ResultReleased = -1;

    /// <summary>Flyleaf refused the release, or could not make it: <c>Activity.RESULT_CANCELED</c>.</summary>
    public const int ResultRefused = 0;

    /// <summary>
    /// Pulp's own: the ordered broadcast's initial code, so a broadcast no receiver answered reads as silence.
    /// Not <c>RESULT_OK</c> (-1) or <c>RESULT_CANCELED</c> (0), the two codes Flyleaf sets.
    /// </summary>
    public const int ResultNoAnswer = 1;

    /// <summary>The longest window Flyleaf honours (its locked session's own cap).</summary>
    public const int MaxMinutes = 360;

    public const int MinMinutes = 1;

    /// <summary>Added to a title's length: the trailers and credits a title's runtime does not count.</summary>
    public const int GraceMinutes = 15;

    /// <summary>The window for a title whose length is not known: a sporting event, or a title with no runtime.</summary>
    public const int UnknownLengthMinutes = 240;

    /// <summary>How long Pulp waits for Flyleaf's answer before it starts the title anyway.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The minutes a release is asked for: the title's runtime plus the grace, or the unknown-length window,
    /// never past <see cref="MaxMinutes"/>. A sports game has no runtime in its feed, so it takes the unknown window.
    /// </summary>
    public static int WindowMinutes(int? runtimeMinutes)
    {
        var length = runtimeMinutes is > 0 ? runtimeMinutes.Value : UnknownLengthMinutes;
        return Math.Min(MaxMinutes, length + GraceMinutes);
    }
}
