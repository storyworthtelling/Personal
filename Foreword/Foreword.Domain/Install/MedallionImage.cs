namespace Foreword.Domain.Install;

/// <summary>Where to cut a square out of a photo, and how big to make it.</summary>
public readonly record struct MedallionPlan(int SourceX, int SourceY, int SourceEdge, int TargetEdge);

/// <summary>
/// Turning a photo off a phone into the mark a page signs off with (DESIGN.md §13).
///
/// <para>Two things have to be true of it, and neither is visible in the picture. It must be
/// <b>square</b>, because the page draws it in a circle, and a rectangle masked to a circle crops
/// whatever happened to be at the edges — usually the top of somebody's head. And it must be
/// <b>small</b>, because it is going into an encrypted vault as base64, and a phone photo is four
/// megabytes before it is encoded.</para>
/// </summary>
public static class MedallionImage
{
    /// <summary>What the foreword page draws it at, in points.</summary>
    public const int PrintedPoints = 96;

    /// <summary>
    /// The longest edge kept. Comfortably more than the printed size, so the mark is not the soft
    /// thing on an otherwise crisp sheet, and small enough that the encoded copy is a rounding
    /// error next to the rest of the vault.
    /// </summary>
    public const int MaxEdge = 256;

    public static MedallionPlan? Plan(int width, int height, int maxEdge = MaxEdge)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var edge = Math.Min(width, height);

        return new MedallionPlan(
            // From the middle. Cropping from a corner is how a photo of two people becomes a photo
            // of one person's shoulder.
            SourceX: (width - edge) / 2,
            SourceY: (height - edge) / 2,
            SourceEdge: edge,

            // Never upscaled: blowing a small photo up adds no detail and costs vault bytes.
            TargetEdge: Math.Min(edge, maxEdge));
    }
}
