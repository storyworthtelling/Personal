using Constitution.Domain.Ground;

namespace Constitution.Domain.Scrapbook;

/// <summary>
/// Maps a crinkle's unitless intensity (<see cref="CrinkleField.MinIntensity"/> to
/// <see cref="CrinkleField.MaxIntensity"/>) onto an ink range of the caller's choosing, which is
/// how the painter turns it into the registered tick alpha. A straight line between the two ends,
/// held at the ends outside the range; the colours and alphas stay in <c>MaterialTokens</c>.
/// </summary>
public static class CrinkleInk
{
    public static double Map(double intensity, double lowest, double highest)
    {
        var across = (intensity - CrinkleField.MinIntensity) / (CrinkleField.MaxIntensity - CrinkleField.MinIntensity);
        return lowest + (Math.Clamp(across, 0, 1) * (highest - lowest));
    }
}
