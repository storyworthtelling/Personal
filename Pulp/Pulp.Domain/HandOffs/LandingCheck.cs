using System.Text;
using Pulp.Domain.Services;

namespace Pulp.Domain.HandOffs;

/// <summary>
/// One reading of a playing app's media session after a hand-off (finding §2.3): which package, when,
/// and — where the app publishes them — the title it named and the position. Read by Pulp's own
/// notification listener on an unrestricted phone, or relayed by Flyleaf on the locked one (Release F).
/// </summary>
public sealed record PlaybackSession
{
    public PlaybackSession(string package, DateTimeOffset at, string? title, TimeSpan? position)
    {
        Package = string.IsNullOrWhiteSpace(package)
            ? throw new ArgumentException("A session reading needs the package that published it.", nameof(package))
            : package.Trim();
        if (position is { } known)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(known, TimeSpan.Zero, nameof(position));
        }

        At = at;
        Title = string.IsNullOrWhiteSpace(title) ? null : title;
        Position = position;
    }

    public string Package { get; }

    public DateTimeOffset At { get; }

    /// <summary>The title the app published; null where it publishes none.</summary>
    public string? Title { get; }

    /// <summary>The playback position; null where the app publishes none.</summary>
    public TimeSpan? Position { get; }
}

/// <summary>Where a hand-off ended up (D122).</summary>
public enum Landing
{
    /// <summary>The service's app played the title it was handed.</summary>
    Landed,

    /// <summary>The service's app played something else: the link fell on its home or another title.</summary>
    LandedElsewhere,

    /// <summary>Nothing seen can tell; Pulp says so rather than guess.</summary>
    Unknown,

    /// <summary>
    /// The window closed with no session at all from the service's app (D286). Not a mis-landing —
    /// a title page waiting for play publishes nothing too — so the dog-ear only says it may not
    /// have landed.
    /// </summary>
    NotSeen,
}

/// <summary>Which evidence decided the landing, so the dog-ear and the diagnostics can say.</summary>
public enum LandingReason
{
    TitleMatched,
    OtherTitlePlaying,
    TitleNotPublished,
    NoNameToMatch,
    NoSessionSeen,
    StillWaiting,
}

public readonly record struct LandingVerdict(Landing Landing, LandingReason Reason);

/// <summary>
/// The mis-landing check (D122, finding §2.4). Only sessions from the handed-off service's own app(s),
/// within <see cref="Window"/> after the hand-off, are evidence:
/// <list type="number">
/// <item>any that names the title → <see cref="Landing.Landed"/> (an auto-played trailer first does not spoil it);</item>
/// <item>else any that names another title → <see cref="Landing.LandedElsewhere"/>, the D122 dog-ear;</item>
/// <item>else sessions with no title or no name to compare → <see cref="Landing.Unknown"/>;</item>
/// <item>no session at all once the window closes → <see cref="Landing.NotSeen"/> (D286).</item>
/// </list>
/// No session is <i>not</i> a mis-landing: a title page that waits for the viewer to press play
/// publishes nothing, exactly like the app's home, and the check cannot tell them apart.
/// </summary>
public static class LandingCheck
{
    /// <summary>
    /// How long after a hand-off a session is still its evidence. <b>Un-evidenced:</b> no service's
    /// session has been read on a device yet (finding §4); this is the value to tune.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(3);

    public static LandingVerdict Decide(
        PendingHandOff handOff,
        IEnumerable<PlaybackSession> sessions,
        ServiceCatalogue catalogue,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(handOff);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(catalogue);

        var from = handOff.Evidence.At;
        var until = from + Window;
        var seen = sessions
            .Where(session => session.At >= from && session.At <= until)
            .Where(session => IsTheServicesApp(session.Package, handOff, catalogue))
            .ToList();

        var titled = seen.Where(session => session.Title is not null).ToList();
        var names = handOff.SessionNames.Select(Normalise).Where(name => name.Length > 0).ToList();

        if (titled.Count > 0 && names.Count == 0)
        {
            return new LandingVerdict(Landing.Unknown, LandingReason.NoNameToMatch);
        }

        if (titled.Any(session => Names(session.Title!, names)))
        {
            return new LandingVerdict(Landing.Landed, LandingReason.TitleMatched);
        }

        if (titled.Count > 0)
        {
            return new LandingVerdict(Landing.LandedElsewhere, LandingReason.OtherTitlePlaying);
        }

        if (seen.Count > 0)
        {
            return new LandingVerdict(Landing.Unknown, LandingReason.TitleNotPublished);
        }

        return now < until
            ? new LandingVerdict(Landing.Unknown, LandingReason.StillWaiting)
            : new LandingVerdict(Landing.NotSeen, LandingReason.NoSessionSeen);
    }

    private static bool IsTheServicesApp(string package, PendingHandOff handOff, ServiceCatalogue catalogue) =>
        string.Equals(package, handOff.Package, StringComparison.OrdinalIgnoreCase)
        || catalogue.ByPackage(package)?.Id.Equals(handOff.Service) == true;

    // Whole words only: "Lantern Cove" is in "S1 E3 · Lantern Cove" but not in "Lantern Coves".
    private static bool Names(string published, IReadOnlyList<string> names)
    {
        var padded = " " + Normalise(published) + " ";
        return names.Any(name => padded.Contains(" " + name + " ", StringComparison.Ordinal));
    }

    private static string Normalise(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }
}
