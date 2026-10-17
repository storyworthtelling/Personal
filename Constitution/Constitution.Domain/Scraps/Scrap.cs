namespace Constitution.Domain.Scraps;

/// <summary>
/// One thing on a page (D47: one thing per scrap). <paramref name="ContentHeightDp"/> is the
/// height the scrap's content measured at the scrap's width, before the scrap's own padding and
/// border; the view measures it, the layout adds the paper around it.
/// </summary>
public sealed record Scrap(ScrapKind Kind, string Id, double ContentHeightDp);
