namespace Constitution.Domain.Recipes.Screens;

/// <summary>
/// Which card is out of its pile. One at a time; a tap on a card brings it out, a tap on it again (its gap, which is the
/// pile) puts it back, and a tap on another card swaps which is out. The animation is the view's.
/// </summary>
public sealed record CardSelection(RecipeId? Picked)
{
    public static CardSelection None { get; } = new((RecipeId?)null);

    public CardSelection Tap(RecipeId card) => Picked == card ? None : new CardSelection(card);

    public CardSelection PutBack() => None;
}

/// <summary>A picked card as it is laid on top (book p.13): its lines, and what it offers about next week.</summary>
public sealed record CardFace(
    string Header,
    string Title,
    string Headline,
    string? Counted,
    string? Reactions,
    string? Facts,
    CookSoonState Cook,
    string CookWords,
    string PutBackHint);

public static class CardPickModel
{
    public const string PutBackWords = "Tap the pile to put it back";

    private const string FamilyCard = "family card";

    public static CardFace Face(Recipe card, CardHistory history, bool plannedNextWeek, DateOnly today) =>
        Face(card, history, plannedNextWeek ? CookSoonState.Planned : CookSoonState.Offer, today);

    public static CardFace Face(Recipe card, CardHistory history, CookSoonState cook, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(history);
        var source = card.SourceLabel is null || card.SourceLabel is RecipeSource.Typed or RecipeSource.Scanned ? FamilyCard : card.SourceLabel;
        return new CardFace(
            Cuisines.Label(card.Cuisine) + " · " + source,
            card.Title,
            CardHistoryWords.Headline(history, today),
            CardHistoryWords.Counted(history),
            CardHistoryWords.Reactions(history),
            CardHistoryWords.Facts(card),
            cook,
            CookSoon.Words(cook),
            PutBackWords);
    }
}
