using Android.Views;
using Constitution.Domain.Scraps;

namespace Constitution.Scrapbook;

/// <summary>
/// One thing on a page, for <see cref="ScrapHost"/> (D47: one thing per scrap). The host measures
/// <paramref name="Content"/> at the scrap's width, asks <c>ScrapLayout</c> where the scrap goes,
/// and sits the content on the paper. <paramref name="Folio"/> is the page number a magazine
/// clipping prints in its corner; the other kinds ignore it.
/// </summary>
public sealed record ScrapEntry(ScrapKind Kind, string Id, View Content, string? Folio = null);
