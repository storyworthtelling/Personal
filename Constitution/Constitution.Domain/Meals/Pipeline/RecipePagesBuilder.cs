using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>One cooked dinner's recipe document, whole: the pages' model and the rating page's model, built from the same facts.</summary>
public sealed record RecipePage(DinnerChoice Dinner, Recipe Card, RecipeDocumentModel Model, RecipeRatingModel Rating);

/// <summary>
/// Builds a cooked dinner's recipe document from its card, the roster and the zone. Pure.
///
/// <para><b>Who is eating.</b> The roster: every adult and every child, once. The planner has no source for which evenings the children are home or
/// for guests, so the page scales for the whole household and says nothing of guests; too many mouths is a leftover, too few is a short supper. The rating
/// page holds <see cref="RecipeRatingPage.MostEaters"/> people, so a larger household is refused with a plain sentence rather than having someone dropped.</para>
///
/// <para><b>The clock.</b> The dinner is where the cook block ends and the first step is worked back from it, the way <see cref="RecipeSchedule"/> does.
/// A step the card gives no minutes for takes <see cref="MissingStepMinutes"/>, the least the clock allows; its hands-on flag stays the card's. Hands-on time is
/// left off the page when no step is hands-on.</para>
///
/// <para><b>The children.</b> A kids' page exists only when the planner says the children eat differently <i>and</i> facts for what they eat were given.
/// A child the kids' dinner names rates that dish, not the adults' (D37).</para>
/// </summary>
public static class RecipePagesBuilder
{
    /// <summary>The minutes a step takes when the card does not say. The clock needs at least one.</summary>
    public const int MissingStepMinutes = 1;

    public const string IngredientsGroup = "Ingredients";

    public static RecipePage Build(DinnerChoice dinner, Recipe card, IReadOnlyList<RosterEntry> people, TimeZoneInfo zone, LastRatings? ratings, KidsMealFacts? kids)
    {
        ArgumentNullException.ThrowIfNull(dinner);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(zone);

        if (dinner.Kind != DinnerKind.Cooked || dinner.Recipe != card.Id)
        {
            throw new ArgumentException($"{dinner.Day.DayOfWeek}'s dinner is not a cooked '{card.Title}', so it has no recipe to print.", nameof(dinner));
        }

        if (card.Steps.Count == 0)
        {
            throw new ArgumentException($"'{card.Title}' has no steps, so there is nothing to put on the clock.", nameof(card));
        }

        var eaters = people.Where(person => person.Role == MemberRole.Adult).Concat(people.Where(person => person.Role == MemberRole.Child)).ToList();
        if (eaters.Count > RecipeRatingPage.MostEaters)
        {
            throw new ArgumentException(
                $"The rating page holds {RecipeRatingPage.MostEaters} eaters and the household has {eaters.Count}; it cannot ask after everyone.", nameof(people));
        }

        var headcount = new RecipeHeadcount(eaters.Count(person => person.Role == MemberRole.Adult), eaters.Count(person => person.Role == MemberRole.Child));
        var kidsMeal = dinner.ChildrenEatDifferently ? kids : null;
        var steps = ClockSteps(card, kidsMeal is not null);
        var total = steps.Sum(step => step.Minutes);
        var handsOn = steps.Where(step => step.Active).Sum(step => step.Minutes);

        var cook = dinner.Cook!;
        var day = dinner.Day.ToDateTime(TimeOnly.MinValue);
        var model = new RecipeDocumentModel(
            card.Title,
            Cuisines.Words.Word(card.Cuisine),
            card.SourceLabel is { } label ? "From the recipe box · " + label : "From the recipe box",
            null,
            cook,
            day,
            Local(dinner.CookEnd!.Value, zone),
            Local(dinner.CookStart!.Value, zone),
            total,
            handsOn == 0 ? null : handsOn,
            card.Servings ?? headcount.Total,
            headcount,
            card.Ingredients.Select(item => new RecipeIngredient(IngredientsGroup, Quantity(item), item.Name)).ToList(),
            card.Steps.Select(step => step.Text).ToList(),
            [],
            1,
            Steps: steps,
            KidsMeal: kidsMeal?.Meal,
            TrailingPages: 1);

        var rows = eaters.Select(person => RatingRow(person, card, kidsMeal, ratings ?? LastRatings.None)).ToList();
        return new RecipePage(dinner, card, model, new RecipeRatingModel(cook, day, rows));
    }

    private static List<RecipeClockStep> ClockSteps(Recipe card, bool handsOverToTheKids)
    {
        var steps = card.Steps
            .Select(step => new RecipeClockStep(step.Text, step.Minutes ?? MissingStepMinutes, step.Active))
            .ToList();
        steps[0] = steps[0] with { CalendarEvent = "Cook: " + card.Title, KidsHandOff = handsOverToTheKids };
        return steps;
    }

    private static RecipeRatingRow RatingRow(RosterEntry person, Recipe card, KidsMealFacts? kids, LastRatings ratings)
    {
        var ateTheKidsDinner = kids is not null && person.Role == MemberRole.Child
            && kids.Meal.Children.Any(name => string.Equals(name?.Trim(), person.Name, StringComparison.OrdinalIgnoreCase));
        var recipe = ateTheKidsDinner ? kids!.Card : card.Id;
        var dish = ateTheKidsDinner ? kids!.Meal.Title : card.Title;
        return new RecipeRatingRow(person.Id, person.Name, recipe, dish, ratings.Of(person.Id, recipe));
    }

    private static string Quantity(Ingredient item) =>
        string.Join(' ', new[] { item.Quantity, item.Unit }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static DateTime Local(DateTimeOffset moment, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(moment, zone).DateTime;
}
