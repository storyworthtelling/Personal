namespace Pulp.Domain.Voice;

/// <summary>One voiced seat: the purpose the composer is given, who hears it, and what shows without a voice.</summary>
public sealed record VoicedOccasion(VoiceSeat Seat, Storyworthtelling.CallSheet.VoiceLine Line, string Decisions)
{
    /// <summary>What the composer is given, from the Call Sheet.</summary>
    public string Purpose => Line.Purpose;

    public VoiceAudience Audience =>
        Line.Audience == Storyworthtelling.CallSheet.VoiceAudience.Child ? VoiceAudience.Child : VoiceAudience.Adult;

    public VoiceFallback Fallback =>
        Line.Fallback == Storyworthtelling.CallSheet.VoiceFallback.Hide ? VoiceFallback.Hide : VoiceFallback.NeutralLine;
}

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
        new(VoiceSeat.Greeting, Sheet("greeting"), "D38, D118"),
        new(VoiceSeat.WhyThis, Sheet("why-this"), "D36, D38, D41"),
        new(VoiceSeat.PlaylistLine, Sheet("playlist-line"), "D38"),
        new(VoiceSeat.ChildGreeting, Sheet("child-greeting"), "D39, D233"),
        new(VoiceSeat.ChildFavorites, Sheet("child-favorites"), "D232, D233"),
        new(VoiceSeat.EmptyContinueWatching, Sheet("empty-continue-watching"), "D264"),
        new(VoiceSeat.EmptyPlaylists, Sheet("empty-playlists"), "D264"),
        new(VoiceSeat.EmptyPlaylist, Sheet("empty-playlist"), "D264"),
        new(VoiceSeat.EmptyRecentlyWatched, Sheet("empty-recently-watched"), "D264"),
        new(VoiceSeat.EmptyChildFavorites, Sheet("empty-child-favorites"), "D264, D233"),
        new(VoiceSeat.EmptyChildWatching, Sheet("empty-child-watching"), "D294, D233"),
        new(VoiceSeat.EmptyChildPlaylists, Sheet("empty-child-playlists"), "D294, D233"),
        new(VoiceSeat.EmptyCalendar, Sheet("empty-calendar"), "D264"),
        new(VoiceSeat.EmptySports, Sheet("empty-sports"), "D264"),
        new(VoiceSeat.StackUsedUp, Sheet("stack-used-up"), "D112, D264"),
    ];

    // Every purpose, audience, fallback and plain line is the suite's voice-line Call Sheet's (audit W10.3).
    private static Storyworthtelling.CallSheet.VoiceLine Sheet(string seat) =>
        Storyworthtelling.CallSheet.CallSheet.For("Pulp", seat);

    private static readonly Dictionary<VoiceSeat, VoicedOccasion> BySeat = All.ToDictionary(occasion => occasion.Seat);

    public static VoicedOccasion For(VoiceSeat seat) =>
        BySeat.TryGetValue(seat, out var occasion)
            ? occasion
            : throw new ArgumentOutOfRangeException(nameof(seat), seat,
                "This seat has no occasion on Pulp's closed list (D38); a voiced seat is a decision, not a diff.");
}
