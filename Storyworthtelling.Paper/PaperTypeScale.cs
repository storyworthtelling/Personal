namespace Storyworthtelling.Paper;

/// <summary>
/// The printed type ladder, in points — the screen ladder multiplied by one registered ratio
/// (DESIGN.md §19, decision D30).
///
/// <para>The sheets used to pick a size at each call site: 8, 9, 10, 11, 12, 16, 20 and 22 points
/// scattered through the composers, none of them derived from anything. That is a <b>second</b> type
/// scale, unregistered, and it is the same shape of problem as a second copy of the palette —
/// nothing compares it to the system it belongs to, so it drifts and the drift is invisible.</para>
///
/// <para>So there is one ladder in the design system, in sp, and paper takes it at
/// <see cref="Ratio"/>. The ratio is registered here and guarded: a test multiplies the native sp
/// ladder — read from its own source file rather than restated — and asserts these are the answers.
/// Changing the printed scale means changing the ratio, once, and seeing every sheet move
/// together.</para>
///
/// <para><b>Why three quarters.</b> A phone is read at arm's length on a backlit panel at roughly
/// 400 pixels per inch; a sheet is read closer, printed far finer, and does not glow. Print type
/// runs smaller than screen type for the same apparent size, and three quarters is both the
/// conventional figure and the one that lands the body role on the 12pt the sheets had already
/// settled on by eye — so the ladder that replaces the literals agrees with the best of them
/// rather than overruling all of them.</para>
/// </summary>
public static class PaperTypeScale
{
    /// <summary>Points per sp. The one number; everything below is derived from it.</summary>
    public const float Ratio = 0.75f;

    public const float Display = 44f * Ratio;      // 33
    public const float Title = 28f * Ratio;        // 21
    public const float Heading = 18f * Ratio;      // 13.5
    public const float Body = 16f * Ratio;         // 12
    public const float Secondary = 14f * Ratio;    // 10.5
    public const float Label = 13f * Ratio;        // 9.75
    public const float Mono = 14f * Ratio;         // 10.5
}
