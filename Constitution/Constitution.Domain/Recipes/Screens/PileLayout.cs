namespace Constitution.Domain.Recipes.Screens;

/// <summary>One card placed in its pile, in dp from the pile's top-left corner. A gap is where a picked card came from.</summary>
public sealed record CardPlacement(Recipe Card, int LeftDp, int TopDp, int WidthDp, int HeightDp, float AngleDegrees, bool IsGap);

/// <summary>A pile placed: its cards, first (top of the visible stack) to last, and how tall the pile is.</summary>
public sealed record PileSpread(IReadOnlyList<CardPlacement> Cards, int HeightDp);

/// <summary>
/// Where a pile's cards lie (C-6.2.1, book p.13, D56-D58): each overlapping the next at a loose angle with its title
/// still readable, no tape. Pure and in dp, so the same pile lays out the same on every phone and the test can hold
/// "every title stays readable" without a screen.
///
/// <para><b>Readable is arithmetic.</b> A card turned by up to <see cref="MaxAngleDegrees"/> lifts or drops a corner by
/// <see cref="RiseDp"/>; the step down to the next card is the readable title row plus that rise twice (once for each
/// card's corner), so a title is never covered by the tilt of the card below it. The angle and sideways drift of a card
/// come from its id, not from chance, so a pile does not reshuffle when it is redrawn.</para>
///
/// <para><b>A picked card leaves its gap.</b> It keeps its place and height in the stack, marked <see cref="CardPlacement.IsGap"/>,
/// so the pile does not close up under the card lying on top of it.</para>
/// </summary>
public static class PileLayout
{
    public const float MaxAngleDegrees = 2.5f;

    /// <summary>The height a title row needs to be read: one line of body type with its padding.</summary>
    public const int ReadableTitleDp = 28;

    /// <summary>The height of a whole card.</summary>
    public const int CardHeightDp = 88;

    /// <summary>Room each side of the cards for their tilt and drift.</summary>
    public const int SideMarginDp = 16;

    private const int DriftDp = 8;

    /// <summary>How far a corner of a card of this width is lifted or dropped by the largest tilt.</summary>
    public static int RiseDp(int widthDp) => (int)Math.Ceiling(widthDp / 2.0 * Math.Sin(MaxAngleDegrees * Math.PI / 180.0));

    public static PileSpread Place(Pile pile, int widthDp, RecipeId? gap)
    {
        ArgumentNullException.ThrowIfNull(pile);
        ArgumentOutOfRangeException.ThrowIfLessThan(widthDp, 4 * SideMarginDp);
        var cardWidth = widthDp - 2 * SideMarginDp;
        var step = ReadableTitleDp + 2 * RiseDp(widthDp);
        var placed = new List<CardPlacement>();
        for (var index = 0; index < pile.Cards.Count; index++)
        {
            var card = pile.Cards[index];
            var seed = Seed(card.Id.Value);
            var angle = ((int)(seed % 51) - 25) / 10f;
            var drift = (int)(seed / 51 % (2 * DriftDp + 1)) - DriftDp;
            placed.Add(new CardPlacement(card, SideMarginDp + drift, index * step, cardWidth, CardHeightDp, angle, gap is { } held && held == card.Id));
        }

        var height = placed.Count == 0 ? 0 : placed[^1].TopDp + CardHeightDp;
        return new PileSpread(placed, height);
    }

    // FNV-1a, because string.GetHashCode differs from one run to the next.
    private static uint Seed(string text)
    {
        var hash = 2166136261u;
        foreach (var character in text)
        {
            hash = (hash ^ character) * 16777619u;
        }

        return hash;
    }
}
