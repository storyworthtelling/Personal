namespace Constitution.Domain.Scraps;

/// <summary>
/// A scrap placed on the page. <paramref name="X"/> and <paramref name="Y"/> are the top-left of
/// the box as laid out, before the tilt; <paramref name="TiltDegrees"/> turns it about its centre,
/// clockwise on screen when positive (as CSS rotate does).
/// </summary>
public sealed record PlacedScrap(
    string Id,
    ScrapKind Kind,
    double X,
    double Y,
    double Width,
    double Height,
    double TiltDegrees,
    TapeAnchor TapeAnchor);
