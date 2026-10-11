namespace Pulp.Domain.Voice;

/// <summary>One voiced seat: the purpose the composer is given, who hears it, and what shows without a voice.</summary>
public sealed record VoicedOccasion(
    VoiceSeat Seat, string Purpose, VoiceAudience Audience, VoiceFallback Fallback, string Decisions);

/// <summary>
/// Pulp's voiced seats, each once (D38, D39, D112, D118, D232, D233, D264, D294).
///
/// <para><b>Errors are not among them, and that is the rule.</b> A voice is for the app's own
/// remarks, not its bad news: a hand-off that did not land, a rejected key, a household file that
/// would not open are facts to act on, and they fold into the dog-ear in plain copy. Dressing them in
/// the owner's wit makes them read as banter, and puts a model call on the path of reporting that
/// something failed.</para>
/// </summary>
public static class VoicedOccasions
{
    public static IReadOnlyList<VoicedOccasion> All { get; } =
    [
        new(VoiceSeat.Greeting,
            "a greeting under the Continue Watching header", VoiceAudience.Adult, VoiceFallback.Hide, "D38, D118"),
        new(VoiceSeat.WhyThis,
            "one line on a title page giving the reason this title was chosen for the viewer",
            VoiceAudience.Adult, VoiceFallback.Hide, "D36, D38, D41"),
        new(VoiceSeat.PlaylistLine,
            "one line about a playlist", VoiceAudience.Adult, VoiceFallback.Hide, "D38"),
        new(VoiceSeat.ChildGreeting,
            "a gentle greeting for a child", VoiceAudience.Child, VoiceFallback.Hide, "D39, D233"),
        new(VoiceSeat.ChildFavorites,
            "one gentle line above a child's favorites", VoiceAudience.Child, VoiceFallback.Hide, "D232, D233"),
        new(VoiceSeat.EmptyContinueWatching,
            "an invitation on a home page with nothing started yet",
            VoiceAudience.Adult, VoiceFallback.NeutralLine, "D264"),
        new(VoiceSeat.EmptyPlaylists,
            "an invitation to make a first playlist", VoiceAudience.Adult, VoiceFallback.NeutralLine, "D264"),
        new(VoiceSeat.EmptyPlaylist,
            "an invitation to put something on a new playlist", VoiceAudience.Adult, VoiceFallback.NeutralLine, "D264"),
        new(VoiceSeat.EmptyRecentlyWatched,
            "a line for a recently-watched page with nothing on it yet",
            VoiceAudience.Adult, VoiceFallback.NeutralLine, "D264"),
        new(VoiceSeat.EmptyChildFavorites,
            "a gentle line for a child whose favorites page has nothing on it yet",
            VoiceAudience.Child, VoiceFallback.NeutralLine, "D264, D233"),
        new(VoiceSeat.EmptyChildWatching,
            "a gentle line for a child whose watching page has nothing started yet",
            VoiceAudience.Child, VoiceFallback.NeutralLine, "D294, D233"),
        new(VoiceSeat.EmptyChildPlaylists,
            "a gentle line for a child whose playlists page has none on it yet",
            VoiceAudience.Child, VoiceFallback.NeutralLine, "D294, D233"),
        new(VoiceSeat.EmptyCalendar,
            "a line for a calendar with nothing coming up", VoiceAudience.Adult, VoiceFallback.NeutralLine, "D264"),
        new(VoiceSeat.EmptySports,
            "an invitation to follow a first team", VoiceAudience.Adult, VoiceFallback.NeutralLine, "D264"),
        new(VoiceSeat.StackUsedUp,
            "a line for a week's stack that has all been seen, until the next one on Friday",
            VoiceAudience.Adult, VoiceFallback.NeutralLine, "D112, D264"),
    ];

    private static readonly Dictionary<VoiceSeat, VoicedOccasion> BySeat = All.ToDictionary(occasion => occasion.Seat);

    public static VoicedOccasion For(VoiceSeat seat) =>
        BySeat.TryGetValue(seat, out var occasion)
            ? occasion
            : throw new ArgumentOutOfRangeException(nameof(seat), seat,
                "This seat has no occasion on Pulp's closed list (D38); a voiced seat is a decision, not a diff.");
}
