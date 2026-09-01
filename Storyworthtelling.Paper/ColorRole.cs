namespace Storyworthtelling.Paper;

/// <summary>
/// A semantic colour role in the P4 design-system expansion (DESIGN.md §3). Code asks for a role;
/// <see cref="PaperPalette.ResolveRole"/> hands back the right colour for the target profile. Roles are
/// named by meaning, never by hue, so a re-tint never renames a token. Additive alongside the legacy
/// document-oriented <see cref="PaperRole"/>; apps adopt this model at their own rebrand phase.
/// </summary>
public enum ColorRole
{
    /// <summary>Pine — the primary action / commit colour, the active thing.</summary>
    Action,

    /// <summary>Leaf green — good news / success.</summary>
    Positive,

    /// <summary>Gray — a settled / closed delta.</summary>
    Resolved,

    /// <summary>Slate-blue — a prior draft, context, structure.</summary>
    Reference,

    /// <summary>Ochre — a "consider" / soft heads-up.</summary>
    Caution,

    /// <summary>Rose — a regression, a contradiction, a blocked item.</summary>
    Problem,

    /// <summary>Teal — currently working / running.</summary>
    InProgress,
}

/// <summary>The five tiers every role carries (DESIGN.md §3): wash · fill · mark · text · deep.</summary>
public enum ColorTier
{
    /// <summary>Translucent highlighter / selection band (the mark hue at <see cref="PaperColorTokens.WashAlpha"/>).</summary>
    Wash,

    /// <summary>Soft fill; chart areas.</summary>
    Fill,

    /// <summary>Colored-pencil stroke, chips, glyph signal.</summary>
    Mark,

    /// <summary>Text on paper — meets AA.</summary>
    Text,

    /// <summary>Deep small-text / dense contexts — AA at any size.</summary>
    Deep,
}

/// <summary>
/// Which surface a role is being resolved for. <see cref="App"/> is the lively on-screen UI; the two
/// document profiles are the muted set — colour for the Paper Pro / Drive PDF, and the separable gray ramp
/// for the rM2. (The App-vs-Document split of DESIGN.md §16.)
/// </summary>
public enum UiColorProfile
{
    /// <summary>The lively on-screen app UI.</summary>
    App,

    /// <summary>Muted colour for the Paper Pro colour e-ink and the Drive/screen PDF.</summary>
    DocumentColor,

    /// <summary>The designed grayscale ramp for the rM2.</summary>
    DocumentGray,
}
