using Constitution.Domain.Scraps;

namespace Constitution.Domain.Scrapbook;

/// <summary>One corner of a cut edge, as the mockup's clip-path writes it: a fraction of the box plus a fixed number of dp.</summary>
public readonly record struct CutVertex(double XFraction, double XDp, double YFraction, double YDp)
{
    public ScrapPoint Resolve(double width, double height) =>
        new((XFraction * width) + XDp, (YFraction * height) + YDp);
}

/// <summary>The scissor, torn and serrated edges of the paper kinds (stub).</summary>
public static class ScrapCuts
{
    public static IReadOnlyList<CutVertex> For(ScrapKind kind) => throw new NotImplementedException();

    public static IReadOnlyList<ScrapPoint> Resolve(ScrapKind kind, double width, double height) =>
        throw new NotImplementedException();
}
