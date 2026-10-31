namespace Constitution.Scrapbook;

/// <summary>
/// Marks a page that lays out across the whole well, edge to edge, because it already carries the
/// well's own padding (<c>ScrapLayout</c> counts the 24 dp and 18 dp in its x). Every other page is
/// a plain sentence or a form and is inset to the screen edge by the area base class.
/// </summary>
public interface IFullBleedPage
{
}
