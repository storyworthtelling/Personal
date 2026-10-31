using Android.Content;
using Android.Views;
using Constitution.Domain.Meals;
using Constitution.Domain.Scraps;
using Constitution.Scrapbook;

namespace Constitution.MealsTab;

/// <summary>
/// Where "I ate something else" writes on the cards of a day (book p.15). A dinner's line goes under the dinner index card's own
/// lines, on the card, so the planned dish stays as it is. Only the dinner has a card (D107): a breakfast or a lunch is recorded and
/// drawn as nothing but the pages' one status sentence. A receipt (an evening out) keeps its own paper, so the dinner line sits on a
/// card beside it.
/// </summary>
internal static class AteCards
{
    public const string ScrapIdPrefix = "ate-";

    /// <summary>The lines the flow has for a day, by meal.</summary>
    public static IReadOnlyDictionary<AteMeal, AteLine> Lines(AteSomethingElseFlow? flow, DateOnly day)
    {
        var lines = new Dictionary<AteMeal, AteLine>();
        if (flow is null)
        {
            return lines;
        }

        foreach (var meal in flow.MealsWithALine(day))
        {
            if (flow.LineFor(meal, day) is { } line)
            {
                lines[meal] = line;
            }
        }

        return lines;
    }

    /// <summary>The dinner card's own line, or null when it has none or the dinner is not on an index card.</summary>
    public static View? OnTheDinnerCard(Context context, IReadOnlyDictionary<AteMeal, AteLine> lines, bool dinnerIsAnIndexCard) =>
        dinnerIsAnIndexCard && lines.TryGetValue(AteMeal.Dinner, out var line) ? AteLineView.Build(context, line) : null;

    /// <summary>The breakfast or lunch status sentence for a day ("Noted for lunch."), or null; the pages draw it inline.</summary>
    public static string? Status(AteSomethingElseFlow? flow, DateOnly day) => flow?.StatusFor(day);

    /// <summary>The dinner's own card when the dinner is not on an index card (a receipt); no other meal has one.</summary>
    public static IEnumerable<ScrapEntry> Extras(Context context, IReadOnlyDictionary<AteMeal, AteLine> lines, bool dinnerIsAnIndexCard)
    {
        if (!dinnerIsAnIndexCard && lines.TryGetValue(AteMeal.Dinner, out var line))
        {
            yield return new ScrapEntry(ScrapKind.IndexCard, ScrapIdPrefix + AteMeals.Word(AteMeal.Dinner), AteLineView.Build(context, line));
        }
    }
}
