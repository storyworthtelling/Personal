namespace Codex.Domain.Music;

/// <summary>How hard the workout is, before the dial refines it.</summary>
public enum TempoBand
{
    Easy,
    Steady,
    Hard,
}

/// <summary>
/// The three bands and the dial they sit on.
///
/// Bands are how you say what you are doing; the dial is how you correct it once you
/// are moving and can feel that 168 is not 172. So the bands are contiguous and cover
/// the whole dial — a tempo the owner can set that no band claims would leave the
/// label under the dial blank.
/// </summary>
public static class TempoBands
{
    /// <summary>The slowest the dial goes — below this it is a walk, not a workout.</summary>
    public const int Slowest = 90;

    /// <summary>The fastest. Past here a runner is matching every other beat anyway.</summary>
    public const int Fastest = 180;

    private const int EasyTop = 125;
    private const int SteadyTop = 150;

    public static (int Low, int High) Range(TempoBand band) => band switch
    {
        TempoBand.Easy => (Slowest, EasyTop),
        TempoBand.Steady => (EasyTop, SteadyTop),
        _ => (SteadyTop, Fastest),
    };

    /// <summary>Where the dial starts for a band: the middle of it, so a correction
    /// goes either way.</summary>
    public static int Default(TempoBand band)
    {
        var (low, high) = Range(band);
        return (low + high) / 2;
    }

    /// <summary>The bands as the segmented control labels them, in band order.</summary>
    public static IReadOnlyList<string> Names { get; } = ["Easy", "Steady", "Hard"];

    public static TempoBand Of(int bpm) => bpm switch
    {
        <= EasyTop => TempoBand.Easy,
        <= SteadyTop => TempoBand.Steady,
        _ => TempoBand.Hard,
    };
}

/// <summary>
/// The tracks that keep step with a tempo, closest first, and whether there are
/// enough of them.
///
/// Half and double time count. A track at 80 keeps step with 160 — every other beat
/// lands — which is ordinary practice for running to music and is most of what saves
/// a pool from being thin. A track nobody has measured (<see cref="Track.Bpm"/> of
/// zero) is not evidence of anything and stays out: unknown is not slow.
/// </summary>
public sealed record TempoPool(IReadOnlyList<Track> Tracks, int Target)
{
    /// <summary>How far off the target still keeps step, in BPM.</summary>
    public const int Tolerance = 6;

    /// <summary>Below this the run loops often enough to notice.</summary>
    public const int Thin = 12;

    public bool IsThin => Tracks.Count < Thin;

    /// <summary>
    /// What to tell Friday, or null when there is enough to play. A <b>measurement</b>
    /// — "there is not enough music at this speed" — and never a preference, which is
    /// what keeps the workout out of the taste profile while still letting it ask for
    /// what it lacks.
    /// </summary>
    public string? Gap => IsThin ? $"more tracks near {Target} BPM" : null;

    public static TempoPool Build(IReadOnlyList<Track> catalog, int targetBpm, int tolerance = Tolerance)
    {
        var matches = catalog
            .Select(track => (Track: track, Distance: DistanceFrom(track.Bpm, targetBpm)))
            .Where(match => match.Distance is not null)
            .OrderBy(match => match.Distance!.Value)
            .ThenBy(match => match.Track.Id, StringComparer.Ordinal)
            .Where(match => match.Distance!.Value <= tolerance)
            .Select(match => match.Track)
            .ToList();

        return new TempoPool(matches, targetBpm);
    }

    // How far a track is from keeping step, counting half and double time. Null when
    // there is no tempo to compare.
    private static int? DistanceFrom(int bpm, int target)
    {
        if (bpm <= 0)
        {
            return null;
        }

        return Math.Min(
            Math.Abs(bpm - target),
            Math.Min(Math.Abs((bpm * 2) - target), Math.Abs((bpm / 2) - target)));
    }
}
