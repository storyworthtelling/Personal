using Constitution.Domain.Scraps;

namespace Constitution.Domain.Scrapbook;

/// <summary>Space on each side of a box, in dp.</summary>
public readonly record struct BoxInsets(double Left, double Top, double Right, double Bottom);

/// <summary>A rectangle in a scrap's own space (origin at its top-left), in dp.</summary>
public readonly record struct PaperRect(double X, double Y, double Width, double Height);

/// <summary>Where a scrap's border, paper and content sit inside its box (stub).</summary>
public static class ScrapBox
{
    public static BoxInsets Border(ScrapKind kind) => throw new NotImplementedException();

    public static BoxInsets Content(ScrapKind kind) => throw new NotImplementedException();

    public static PaperRect Paper(ScrapKind kind, double width, double height) => throw new NotImplementedException();
}
