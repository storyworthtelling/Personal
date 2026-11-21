using Watch.Domain.Face;

namespace Watch.Domain.Kit;

/// <summary>Where the wave lies: its left end, its width and its resting line, in pixels (y grows down).</summary>
public readonly record struct WaveFrame(double Left, double Width, double Baseline);

/// <summary>Where a tap on the wave lands (A72): the left edge goes back, the right edge skips, the rest pauses or plays.</summary>
public enum WaveTarget
{
    Back,
    PlayPause,
    Skip,
}

/// <summary>
/// Wave's player is its sine (A72, mock: Now playing / Paused / Turning the crown; P14 W-1.2.1): two
/// periods across the span, bright as far as the track has played, its height the volume, flat when
/// paused. Amplitudes are fractions of the face's radius; points are in the frame's pixels.
/// </summary>
public static class WaveGeometry
{
    /// <summary>Sine periods across the span (mock: <c>waveLine</c>).</summary>
    public const int Periods = 2;

    /// <summary>Straight pieces the wave is drawn with (mock: n = 120).</summary>
    public const int Segments = 120;

    /// <summary>The shallowest wave, at the lowest volume: still a wave, so it never reads as paused.</summary>
    public const double Quietest = 0.04;

    /// <summary>The tallest wave, at full volume (the crown mock draws 0.34).</summary>
    public const double Loudest = 0.36;

    /// <summary>One crown detent's change in height.</summary>
    public const double Step = 0.02;

    /// <summary>Each edge's share of the width that goes back or skips (at least a touch target).</summary>
    public const double EdgeShare = 0.2;

    /// <summary>The most an edge may take, so a play/pause middle always remains.</summary>
    public const double EdgeCap = 0.4;

    /// <summary>The wave's width as a multiple of the face's radius, centred (mock: Now playing).</summary>
    public const double Span = 1.6;

    /// <summary>The wave's stroke, as a fraction of the face's radius.</summary>
    public const double Stroke = 0.055;

    /// <summary>The played part's glow, as a fraction of the face's radius.</summary>
    public const double Glow = 0.08;

    /// <summary>The back and skip marks' stroke, as a fraction of the face's radius.</summary>
    public const double MarkStroke = 0.03;

    /// <summary>The back mark, a short dash on the left (never an arrow), across the face's diameter in radii.</summary>
    public static (double From, double To) BackMark { get; } = (0.08, 0.16);

    /// <summary>The skip mark, a short dash on the right.</summary>
    public static (double From, double To) SkipMark { get; } = (1.84, 1.92);

    /// <summary>Where the wave starts on a face of <paramref name="radius"/>.</summary>
    public static double LeftOn(double radius) => radius - radius * Span / 2;

    /// <summary>The height for a volume from 0 to 1 (clamped).</summary>
    public static double AmplitudeFor(double volume) =>
        Quietest + (Loudest - Quietest) * Math.Clamp(volume, 0, 1);

    /// <summary>The crown turned by <paramref name="detents"/> (negative is quieter), held between the ends.</summary>
    public static double Nudge(double amplitude, int detents) =>
        Math.Clamp(amplitude + detents * Step, Quietest, Loudest);

    /// <summary>The whole wave, left to right, <see cref="Segments"/> + 1 points; the first crest rises up.</summary>
    public static IReadOnlyList<ShapePoint> Points(WaveFrame frame, double amplitude, bool paused) =>
        Walk(frame, amplitude, paused, Segments + 1);

    /// <summary>
    /// The part that has played: the first <c>round(Segments × progress) + 1</c> points of the wave, or
    /// nothing at the very start. Progress is clamped to 0–1.
    /// </summary>
    public static IReadOnlyList<ShapePoint> Bright(WaveFrame frame, double amplitude, bool paused, double progress)
    {
        var count = (int)Math.Round(Segments * Math.Clamp(progress, 0, 1)) + 1;
        return count < 2 ? [] : Walk(frame, amplitude, paused, count);
    }

    /// <summary>What a tap at <paramref name="x"/> across a wave <paramref name="width"/> wide means.</summary>
    public static WaveTarget TargetAt(double x, double width, double touchTarget)
    {
        var edge = Math.Min(Math.Max(width * EdgeShare, touchTarget), width * EdgeCap);
        if (x < edge)
        {
            return WaveTarget.Back;
        }
        return x > width - edge ? WaveTarget.Skip : WaveTarget.PlayPause;
    }

    private static ShapePoint[] Walk(WaveFrame frame, double amplitude, bool paused, int count)
    {
        var points = new ShapePoint[count];
        for (var index = 0; index < count; index++)
        {
            var along = (double)index / Segments;
            var lift = paused ? 0 : Math.Sin(along * 2 * Math.PI * Periods) * amplitude;
            points[index] = new ShapePoint(frame.Left + along * frame.Width, frame.Baseline - lift);
        }
        return points;
    }
}
