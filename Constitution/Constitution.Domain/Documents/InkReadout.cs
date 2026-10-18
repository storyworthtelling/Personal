namespace Constitution.Domain.Documents;

/// <summary>The reader's side of the map: which target a mark landed in.</summary>
public sealed class InkReadout
{
    public InkReadout(InkMap map) => Map = map;

    /// <summary>The very map the painter drew from, not a copy of it.</summary>
    public InkMap Map { get; }

    /// <summary>The target holding a point, or null for a mark in the blank.</summary>
    public InkTarget? TargetAt(float x, float y) => Map.Targets.FirstOrDefault(t => t.Bounds.Contains(x, y));

    /// <summary>
    /// Every target a mark touches, in page order. A circle across two lanes touches both, which the
    /// reader turns into a confirm card instead of guessing.
    /// </summary>
    public IReadOnlyList<InkTarget> TargetsTouching(InkRect mark) =>
        Map.Targets.Where(t => t.Bounds.Overlaps(mark)).ToList();
}
