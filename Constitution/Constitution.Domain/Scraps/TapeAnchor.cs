namespace Constitution.Domain.Scraps;

/// <summary>
/// The strip of Scotch tape holding a scrap (D48), already resolved into the well's space: where
/// its centre is once the scrap has been turned, how big it is, and how far it is turned in all
/// (the scrap's tilt plus the tape's own).
/// </summary>
public sealed record TapeAnchor(double CenterX, double CenterY, double WidthDp, double HeightDp, double RotationDegrees);
