namespace Constitution.Domain.Recipes.Screens;

public sealed record CuisineRow(Cuisine Cuisine, string Label, bool IsEnabled, string? Reason);

public sealed record CuisineCard(string Title, string Subtitle, IReadOnlyList<CuisineRow> Rows);

/// <summary>The action card a long-press on a card raises (C-6.2.4): the cuisines to file it under, the current one greyed with its reason and none hidden.</summary>
public static class CuisineChoices
{
    public const string Heading = "Move to another cuisine";
    public const string HereReason = "Filed here now";

    public static CuisineCard For(Recipe card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var rows = Cuisines.All
            .Select(cuisine => cuisine == card.Cuisine
                ? new CuisineRow(cuisine, Cuisines.Label(cuisine), false, HereReason)
                : new CuisineRow(cuisine, Cuisines.Label(cuisine), true, null))
            .ToList();
        return new CuisineCard(Heading, card.Title, rows);
    }
}
