namespace Folio.Domain.Report;

/// <summary>
/// The three tones the owner's mark is drawn in on a report cover: the oval's fill, the oval's own
/// outline, and the M that pierces it. Channels are 0xRRGGBB.
/// </summary>
public sealed record MarkColours(int Ground, int Line, int Ink);

/// <summary>
/// The mark takes <b>its own report's jacket</b>, and nothing else decides its colour (F37).
///
/// <para>The mark is drawn rather than photographed, so for the first time it has a colourway to
/// choose — and the obvious answer, a constant, is the wrong one: every report in the shelf would
/// then carry the same stamp regardless of the book it is about. So the two most prominent tones in
/// the jacket become the oval and the M, and a new jacket recolours the mark with nobody choosing a
/// colour.</para>
///
/// <para><b>The contrast floor is why this is a function and not a pick.</b> Two prominent tones in
/// a painting are frequently near-neighbours — a jacket that is all marsh and all dusk has no
/// natural pair — and an M drawn in one on a ground of the other would be a smudge. So the pair is
/// measured (WCAG relative luminance, §17's AA at 4.5:1, which the drawn mark clears at 6.08) and,
/// when it does not clear, the lighter tone is carried further toward white or the darker toward
/// black along its own hue until it does. The jacket still chooses; it just cannot choose something
/// illegible.</para>
/// </summary>
public static class MarkColourway
{
    /// <summary>§17 AA for text. The M is a letterform, so it is held to the text floor, not 3:1.</summary>
    public const double ContrastFloor = 4.5;

    /// <summary>How far the oval's outline is carried from its fill, as a fraction of the distance.</summary>
    private const double LineShift = 1.0 / 3.0;

    /// <summary>Bits kept per channel when the jacket is quantised — 32 levels, ~32k buckets.</summary>
    private const int QuantisedBits = 3;

    public static MarkColours From(IReadOnlyList<int> pixels)
    {
        if (pixels is not { Count: > 0 })
        {
            throw new ArgumentException(
                "A jacket with no pixels cannot colour the mark. Every book has a jacket by the time a "
                + "report is composed — the plain cloth fallback included — so an empty image here is a "
                + "decode that failed silently upstream, and a constant colourway would hide it.",
                nameof(pixels));
        }

        var counts = new Dictionary<int, int>();
        foreach (var pixel in pixels)
        {
            var key = Quantise(pixel);
            counts[key] = counts.TryGetValue(key, out var seen) ? seen + 1 : 1;
        }

        var ranked = counts.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key)
            .Select(entry => entry.Key).ToList();

        var ground = ranked[0];

        // The partner is the most prominent tone that already clears the floor against the ground; if
        // none does, the most prominent tone furthest from it in luminance, which is then carried.
        var ink = ranked.Skip(1).FirstOrDefault(
            candidate => Contrast(ground, candidate) >= ContrastFloor, defaultValue: -1);
        if (ink < 0)
        {
            ink = ranked.Count > 1
                ? ranked.Skip(1).MaxBy(candidate => Math.Abs(Luminance(candidate) - Luminance(ground)))
                : ground;
            ink = Separate(ground, ink);
        }

        return new MarkColours(ground, Outline(ground), ink);
    }

    /// <summary>
    /// Carries <paramref name="ink"/> away from <paramref name="ground"/> along its own hue until the
    /// pair clears the floor.
    ///
    /// <para><b>The direction is not the side the ink started on.</b> That was the first rule written
    /// here and a mid-grey jacket disproves it: against <c>#8A8A8A</c>, pure white reaches only
    /// 3.43:1, so an ink that begins a shade lighter can be carried the whole way to white and still
    /// be a smudge. Black against that same grey is 6.12:1. Every colour clears the floor against at
    /// least one of the two limits — an inevitability of the ratio, since a ground cannot be close to
    /// both — so the direction is chosen by which limit actually works, keeping the ink's original
    /// side when both do.</para>
    /// </summary>
    private static int Separate(int ground, int ink)
    {
        var lighter = Luminance(ink) >= Luminance(ground);
        var first = lighter ? 0xFFFFFF : 0x000000;
        var second = lighter ? 0x000000 : 0xFFFFFF;

        return Carry(ground, ink, first)
            ?? Carry(ground, ink, second)
            ?? (Contrast(ground, first) >= Contrast(ground, second) ? first : second);
    }

    private static int? Carry(int ground, int ink, int toward)
    {
        for (var step = 1; step <= 100; step++)
        {
            var candidate = Mix(ink, toward, step / 100.0);
            if (Contrast(ground, candidate) >= ContrastFloor)
            {
                return candidate;
            }
        }
        return null;
    }

    // The oval's own outline: its fill carried a third of the way toward black, or toward white when
    // the fill is already dark enough that black would lose the edge entirely.
    private static int Outline(int ground) =>
        Mix(ground, Luminance(ground) > 0.06 ? 0x000000 : 0xFFFFFF, LineShift);

    private static int Quantise(int rgb)
    {
        var mask = 0xFF << QuantisedBits & 0xFF;
        // Quantising to the bottom of a bucket biases the whole palette dark; take the bucket's middle.
        var half = 1 << (QuantisedBits - 1);
        var r = Math.Min(255, (Red(rgb) & mask) + half);
        var g = Math.Min(255, (Green(rgb) & mask) + half);
        var b = Math.Min(255, (Blue(rgb) & mask) + half);
        return r << 16 | g << 8 | b;
    }

    private static int Mix(int from, int to, double amount)
    {
        var r = (int)Math.Round(Red(from) + (Red(to) - Red(from)) * amount);
        var g = (int)Math.Round(Green(from) + (Green(to) - Green(from)) * amount);
        var b = (int)Math.Round(Blue(from) + (Blue(to) - Blue(from)) * amount);
        return r << 16 | g << 8 | b;
    }

    public static double Contrast(int a, int b)
    {
        var (high, low) = Luminance(a) >= Luminance(b)
            ? (Luminance(a), Luminance(b))
            : (Luminance(b), Luminance(a));
        return (high + 0.05) / (low + 0.05);
    }

    private static double Luminance(int rgb) =>
        0.2126 * Channel(Red(rgb)) + 0.7152 * Channel(Green(rgb)) + 0.0722 * Channel(Blue(rgb));

    private static double Channel(int value)
    {
        var c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static int Red(int rgb) => rgb >> 16 & 0xFF;
    private static int Green(int rgb) => rgb >> 8 & 0xFF;
    private static int Blue(int rgb) => rgb & 0xFF;
}
