namespace Index.Domain.Worksheet;

/// <summary>
/// D154: every figure printed on the worksheet is whole dollars, rounded DOWN — never to nearest,
/// never with cents.
///
/// <para><b>Guard G19's exact failure mode:</b> flooring a combined total computed from unrounded
/// numbers can disagree with the sum of the parts' OWN already-floored figures by up to a dollar,
/// whenever fractional cents accumulate past a whole dollar between the parts (three parts at .50
/// each floor individually to 0 lost each, but their true sum's fractional part carries a whole
/// dollar of its own). A printed page where the visible lines add up to one figure while the page's
/// own total reads a dollar different looks like a slip in arithmetic on paper, when it is really two
/// different roundings of the same underlying number — so a composer must always build a printed
/// total from the SUM of the already-floored, already-printed parts, never floor the raw combination
/// on its own.</para>
/// </summary>
public static class WholeDollars
{
    public static long Floor(decimal amount) => (long)Math.Floor(amount);

    public static long SumOfFlooredParts(IEnumerable<decimal> parts) => parts.Sum(Floor);
}
