using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>The four inks one bound spine needs.</summary>
public readonly record struct SpineCloth(Color Top, Color Bottom, Color Hinge, Color Lettering);

/// <summary>
/// What colour a book is bound in.
///
/// <para><b>Provisional, and the frame says so.</b> Its open questions include "cloth colour is still
/// undecided — these are plausible, not generated", so what is here is a deterministic stand-in
/// rather than a decision: the same book is always the same colour, which is what a shelf needs, and
/// nothing about the choice is meaningful yet.</para>
///
/// <para><b>Token-only</b>, following the precedent set by Flyleaf's shelf: every tone is a role or
/// identity token, never a hex picked to look nice. That is what keeps the leak guard meaningful
/// and what will make the real decision a change in one place rather than a hunt.</para>
///
/// <para>Keyed by title with the same FNV-1a hash the spine heights use, for the same reason —
/// <c>string.GetHashCode</c> is randomised per process in .NET Core, so a shelf keyed on it would
/// rebind every book on every launch.</para>
/// </summary>
public static class BookCloth
{
    public static Color Bronze { get; } = Color.FromArgb(PaperColorTokens.CautionFill);

    public static Color BronzeEdge { get; } = Color.FromArgb(PaperColorTokens.CautionDeep);

    // Mark over Deep: a bound cloth catching light at the top and falling into shadow at the foot.
    private static readonly (string Mark, string Deep, bool DarkCloth)[] Cloths =
    [
        (PaperColorTokens.ReferenceMark, PaperColorTokens.ReferenceDeep, true),
        (PaperColorTokens.ActionMark, PaperColorTokens.ActionDeep, true),
        (PaperColorTokens.ProblemMark, PaperColorTokens.ProblemDeep, true),
        (PaperColorTokens.ProgressMark, PaperColorTokens.ProgressDeep, true),
        (AppIdentityTokens.FolioMarkHex, AppIdentityTokens.FolioDeepHex, true),
        (PaperColorTokens.CautionMark, PaperColorTokens.CautionDeep, true),
        // Two pale bindings, so a shelf is not uniformly dark. These letter in ink, not cream.
        (PaperColorTokens.PaperShade, PaperColorTokens.CautionFill, false),
        (AppIdentityTokens.FolioFillHex, AppIdentityTokens.FolioGroundHex, false),
    ];

    public static SpineCloth For(string title)
    {
        var (mark, deep, dark) = Cloths[(int)(Hash(title) % (uint)Cloths.Length)];

        return new SpineCloth(
            Top: Color.FromArgb(mark),
            Bottom: Color.FromArgb(deep),
            Hinge: dark
                ? Color.FromArgb(PaperColorTokens.Paper).WithAlpha(0.35f)
                : Color.FromArgb(PaperColorTokens.Ink).WithAlpha(0.22f),
            Lettering: Color.FromArgb(dark ? PaperColorTokens.Paper : PaperColorTokens.Ink));
    }

    private static uint Hash(string title)
    {
        var hash = 2_166_136_261u;
        foreach (var character in title ?? string.Empty)
        {
            hash = (hash ^ character) * 16_777_619u;
        }

        return hash;
    }
}
