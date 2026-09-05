namespace Codex.Domain.Apps;

/// <summary>
/// Maps per-app usage into spine-size buckets so the tray's book spines vary by how much the
/// owner actually reaches for each app (taller/thicker = used more) — the shelf reads like a
/// well-thumbed bookcase. Sizing is <em>relative to the busiest app</em>, not an absolute
/// threshold, so a lightly-used phone still spreads across the buckets. When usage is empty —
/// the on-device usage-access permission wasn't granted — every spine falls to <see cref="Size.Slim"/>
/// (uniform), which is the graceful default. Pure so it's unit-tested without a device.
/// </summary>
public static class SpineSizing
{
    /// <summary>Spine prominence, slimmest to broadest.</summary>
    public enum Size
    {
        Slim,
        Regular,
        Wide,
        Broad,
    }

    /// <summary>
    /// Assigns each requested package a size from its usage weight (e.g. foreground time or
    /// launch count) relative to the busiest requested app. Packages absent from
    /// <paramref name="usageByPackage"/>, or with zero weight, are <see cref="Size.Slim"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, Size> Rank(
        IReadOnlyDictionary<string, long> usageByPackage,
        IReadOnlyList<string> packages)
    {
        var max = packages.Count == 0 ? 0 : packages.Max(Weight);
        return packages.ToDictionary(package => package, package => Bucket(Weight(package), max));

        long Weight(string package) =>
            usageByPackage.TryGetValue(package, out var w) && w > 0 ? w : 0;
    }

    private static Size Bucket(long weight, long max)
    {
        if (weight <= 0 || max <= 0)
        {
            return Size.Slim;
        }
        var ratio = (double)weight / max;
        return ratio switch
        {
            <= 0.20 => Size.Slim,
            <= 0.45 => Size.Regular,
            <= 0.75 => Size.Wide,
            _ => Size.Broad,
        };
    }
}
