namespace Storyworthtelling.Paper;

/// <summary>
/// The colour target a document is being rendered for. The renderer resolves every semantic
/// <see cref="PaperRole"/> through <see cref="PaperPalette"/> against one of these, and delivery picks the
/// profile per destination (Paper Pro → colour, rM2 → grayscale, Drive/in-app → RGB). One document is
/// described once in semantic roles; the profile is the only thing that changes between surfaces.
/// </summary>
public enum DeviceColorProfile
{
    /// <summary>reMarkable Paper Pro — the calm colour palette on warm colour e-ink.</summary>
    PaperPro,

    /// <summary>reMarkable rM2 — the designed grayscale ramp on grayscale e-ink.</summary>
    Rm2,

    /// <summary>The Drive PDF and in-app view — full RGB on a bright ground.</summary>
    Screen,
}

/// <summary>
/// A colour's meaning in a composed document, independent of the literal hue. The renderer asks for a role;
/// <see cref="PaperPalette"/> hands back the right colour for the target device. Nothing above the palette
/// deals in hex.
/// </summary>
public enum PaperRole
{
    /// <summary>The page ground (paper colour), which varies by device.</summary>
    Ground,

    /// <summary>Body text / rules.</summary>
    Ink,

    /// <summary>Secondary text.</summary>
    InkSoft,

    /// <summary>Hairlines / faint dividers.</summary>
    Hairline,

    /// <summary>The accent — the active line, the current draft, the one thing being emphasised.</summary>
    Active,

    /// <summary>Structure / a prior draft / a reference (slate).</summary>
    Prior,

    /// <summary>Positive — a new scene, a resolved finding (sage).</summary>
    New,

    /// <summary>A problem — a regression, a contradiction (plum).</summary>
    Problem,

    /// <summary>Caution — a "consider" note (ochre).</summary>
    Caution,
}
