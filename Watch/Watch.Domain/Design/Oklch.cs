namespace Watch.Domain.Design;

/// <summary>A colour in OKLCH: lightness 0–1, chroma, hue in degrees 0–360.</summary>
public readonly record struct Oklch(double L, double C, double H);
