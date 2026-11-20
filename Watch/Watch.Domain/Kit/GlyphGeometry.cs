namespace Watch.Domain.Kit;

/// <summary>
/// The kit's shapes as fractions (WD §3; mock: <c>glyph</c>, <c>ring</c>, "Filed, with Undo"). Glyph
/// figures are fractions of the glyph's side; ring figures are fractions of the face's radius, except
/// <see cref="UndoRingRadius"/>, which is a fraction of the undo glyph's side.
/// </summary>
public static class GlyphGeometry
{
    /// <summary>Dash: one short horizontal dash with round ends, from 0.3 to 0.7 of the side.</summary>
    public const double DashFrom = 0.3;

    public const double DashTo = 0.7;

    /// <summary>The dash's stroke: 14% of the glyph's width (WD §3).</summary>
    public const double DashStroke = 0.14;

    /// <summary>A glyph's glow in its own colour, as a fraction of its side.</summary>
    public const double Glow = 0.12;

    /// <summary>The undo glyph's side, as a fraction of the face's radius.</summary>
    public const double UndoGlyph = 0.5;

    /// <summary>The undo ring's radius, as a fraction of the undo glyph's side.</summary>
    public const double UndoRingRadius = 0.62;

    /// <summary>The undo ring's stroke, as a fraction of the face's radius.</summary>
    public const double UndoRingStroke = 0.014;

    /// <summary>A progress ring's radius (cadence, the rest timer), as a fraction of the face's radius.</summary>
    public const double RingRadius = 0.93;

    /// <summary>A progress ring's stroke, as a fraction of the face's radius.</summary>
    public const double RingStroke = 0.03;
}
