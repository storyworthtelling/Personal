namespace Constitution.Domain.Documents;

/// <summary>A rectangle in page points from the top left, the unit the tablet's glass uses.</summary>
public readonly record struct InkRect(float Left, float Top, float Width, float Height)
{
    /// <summary>Float slack: 18 + 8 = 26 must read as exactly clear, not as a hair's overlap.</summary>
    private const float Epsilon = 0.001f;

    public float Right => Left + Width;
    public float Bottom => Top + Height;
    public float CentreX => Left + Width / 2f;
    public float CentreY => Top + Height / 2f;

    public InkRect Inflate(float by) => new(Left - by, Top - by, Width + 2f * by, Height + 2f * by);

    /// <summary>True when the two share area or a strictly interior line; touching edges do not overlap.</summary>
    public bool Overlaps(InkRect other) =>
        Left < other.Right - Epsilon && other.Left < Right - Epsilon
        && Top < other.Bottom - Epsilon && other.Top < Bottom - Epsilon;

    public bool Contains(float x, float y) =>
        x >= Left - Epsilon && x <= Right + Epsilon && y >= Top - Epsilon && y <= Bottom + Epsilon;

    public bool Encloses(InkRect inner) =>
        inner.Left >= Left - Epsilon && inner.Top >= Top - Epsilon
        && inner.Right <= Right + Epsilon && inner.Bottom <= Bottom + Epsilon;
}
