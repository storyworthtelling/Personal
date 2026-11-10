namespace Foreword.Domain.Brief;

/// <summary>One kind of paper in the morning brief, and the name its tab links to.</summary>
public sealed record BriefTab(string Label, string Destination);

/// <summary>
/// The morning brief's binder tabs (§10), by the owner's decision: News, Opinion, Newsletters, Comics, Games, in that
/// order. It is an open list — a new kind of paper joins by being named here, between the ones it belongs with — and a
/// tab is drawn only on a morning its section has pages, so a section with no source yet (Newsletters) waits invisibly.
/// </summary>
public static class BriefTabs
{
    public static readonly BriefTab News = Tab("News");
    public static readonly BriefTab Opinion = Tab("Opinion");
    public static readonly BriefTab Newsletters = Tab("Newsletters");
    public static readonly BriefTab Comics = Tab("Comics");
    public static readonly BriefTab Games = Tab("Games");

    public static readonly IReadOnlyList<BriefTab> All = [News, Opinion, Newsletters, Comics, Games];

    /// <summary>The tabs of a morning whose sections have these pages, in the list's order.</summary>
    public static IReadOnlyList<BriefTab> Present(IReadOnlySet<BriefTab> withPages) => All.Where(withPages.Contains).ToList();

    /// <summary>Namespaced away from The Day's and Prayer's, so no two documents share a destination.</summary>
    private static BriefTab Tab(string label) => new(label, $"foreword-brief-{label}".ToLowerInvariant());
}
