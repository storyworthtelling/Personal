using Codex.Domain.Design;

namespace Constitution.Domain.Areas;

/// <summary>
/// The five areas in strip order (D40, D45, book p.11). Ids are literals: they are stored and
/// routed on, so a renamed identifier must not rename them.
/// </summary>
public static class ConstitutionAreas
{
    public static IReadOnlyList<ConstitutionArea> All { get; } =
    [
        new("today", "Today", DesignTokens.AreaCloths.Constitution.Today,
            DesignTokens.Paper, DesignTokens.Identity.Constitution.Text, null),
        new("workouts", "Workouts", DesignTokens.AreaCloths.Constitution.Workouts,
            DesignTokens.Paper, DesignTokens.AreaCloths.Constitution.Workouts, pillars => pillars.Workouts),
        new("meals", "Meals", DesignTokens.AreaCloths.Constitution.Meals,
            DesignTokens.Paper, DesignTokens.AreaCloths.Constitution.Meals, pillars => pillars.Meals),
        new("body", "Body", DesignTokens.AreaCloths.Constitution.Body,
            DesignTokens.Paper, DesignTokens.AreaCloths.Constitution.Body, pillars => pillars.Body),
        new("settings", "Settings", DesignTokens.AreaCloths.Constitution.Settings,
            DesignTokens.Ink, DesignTokens.Ink, null),
    ];

    /// <summary>The tabs to draw for these pillars, in order; an off pillar's tab is simply absent.</summary>
    public static IReadOnlyList<ConstitutionArea> Visible(Pillars pillars) =>
        All.Where(area => area.Pillar is null || area.Pillar(pillars)).ToList();
}
