namespace Pulp.Domain.Discover;

/// <summary>What a drag across a pile's front poster asked for.</summary>
public enum DiscoverSwipe
{
    None,
    Next,
    Previous,
}

/// <summary>
/// A pile as a literal stack (D219): one poster at the front, at most two fanned behind it as mockup 03
/// draws them, a swipe to flip to the next (round to the first after the last, since the cards are a
/// stack rather than a list with ends), and "1 of 3" saying where you are.
/// </summary>
public static class DiscoverPagePile
{
    /// <summary>Mockup 03 fans two cards behind a pile of three and one behind a pile of two.</summary>
    public const int MaxFannedBehind = 2;

    public static string Position(int index, int count)
    {
        Check(index, count);
        return $"{index + 1} of {count}";
    }

    public static int Next(int index, int count)
    {
        Check(index, count);
        return (index + 1) % count;
    }

    public static int Previous(int index, int count)
    {
        Check(index, count);
        return (index - 1 + count) % count;
    }

    public static int FannedBehind(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return Math.Clamp(count - 1, 0, MaxFannedBehind);
    }

    /// <summary>
    /// A drag is a swipe when it travelled at least <paramref name="threshold"/> sideways and more
    /// sideways than up or down — anything else is left to the page's own scrolling. Leftward turns
    /// to the next card, as a hand pushes the front card aside.
    /// </summary>
    public static DiscoverSwipe Swipe(float dx, float dy, float threshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threshold);
        if (Math.Abs(dx) < threshold || Math.Abs(dx) <= Math.Abs(dy))
        {
            return DiscoverSwipe.None;
        }

        return dx < 0 ? DiscoverSwipe.Next : DiscoverSwipe.Previous;
    }

    private static void Check(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
    }
}
