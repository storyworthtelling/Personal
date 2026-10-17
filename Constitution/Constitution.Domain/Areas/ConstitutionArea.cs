namespace Constitution.Domain.Areas;

/// <summary>
/// One binder tab. <see cref="Cloth"/> is the inactive tab's cloth, <see cref="InactiveLettering"/>
/// the glyph and label on it, and <see cref="ActiveInk"/> the ink the active (page-coloured) tab
/// takes. All three are token values from <c>DesignTokens</c>, never literals here.
/// </summary>
/// <param name="Pillar">The pillar that shows this tab, or null for an area that is always shown.</param>
public sealed record ConstitutionArea(
    string Id,
    string Name,
    string Cloth,
    string InactiveLettering,
    string ActiveInk,
    Func<Pillars, bool>? Pillar);
