using Folio.Domain.Readiness;

namespace Folio.Domain.Report;

/// <summary>
/// How a severity is drawn apart from its colour: a weight and a dash pattern (empty = solid).
/// </summary>
public readonly record struct SeverityStroke(double Width, IReadOnlyList<double> Dash)
{
    public bool IsSolid => Dash.Count == 0;
}

/// <summary>
/// DESIGN.md §16's non-colour cue, for the one place in the report that needed it first: four
/// severities drawn along a single line, where hue is the thing that cannot be trusted.
///
/// <para>Each tier gets a weight <i>and</i> a dash, because neither alone separates four things on a
/// printed line — four weights are four guesses at a glance, and four dashes at one weight read as
/// one texture. Together they are four strokes nobody has to compare to tell apart, and all four are
/// distinct from the plain line the chart draws where nothing is wrong.</para>
/// </summary>
public static class SeverityCue
{
    /// <summary>The tension line where no finding sits: thin and solid, so it recedes.</summary>
    public static readonly SeverityStroke Plain = new(1.0, []);

    public static SeverityStroke Stroke(ReadinessTier tier) => tier switch
    {
        // Heaviest and unbroken: a Must is the one a reader should see without looking for it.
        ReadinessTier.Must => new(2.6, []),
        ReadinessTier.Should => new(2.0, [7, 3]),
        ReadinessTier.Consider => new(1.6, [2, 3]),
        // A dash-dot, which is the conventional "and this part is working" without being a fifth
        // weight nobody can place.
        ReadinessTier.Great => new(1.6, [9, 3, 2, 3]),
        _ => Plain,
    };
}
