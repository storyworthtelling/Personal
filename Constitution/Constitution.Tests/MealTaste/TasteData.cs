using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;

namespace Constitution.Tests.MealTaste;

/// <summary>Invented people and invented dishes for the taste tests. Nothing here is a real person or a real recipe.</summary>
internal static class TasteData
{
    public static readonly PersonId Sam = new("sam");
    public static readonly PersonId Lee = new("lee");
    public static readonly PersonId Robin = new("robin");
    public static readonly PersonId Juniper = new("juniper");

    public static readonly RecipeId Risotto = new("mushroom-risotto");
    public static readonly RecipeId Pasta = new("tomato-pasta");
    public static readonly RecipeId Curry = new("lentil-curry");
    public static readonly RecipeId Burritos = new("bean-burritos");
    public static readonly RecipeId Tacos = new("corn-tacos");
    public static readonly RecipeId Omelette = new("herb-omelette");
    public static readonly RecipeId Tart = new("onion-tart");
    public static readonly RecipeId Chicken = new("roast-chicken");

    /// <summary>A Monday the ratings hang from.</summary>
    public static readonly DateOnly Week = new(2026, 10, 19);

    public static DateOnly Day(int offset) => Week.AddDays(offset);

    public static IReadOnlyList<TastePerson> People() =>
    [
        new(Sam, "Sam", MemberRole.Adult),
        new(Lee, "Lee", MemberRole.Adult),
        new(Robin, "Robin", MemberRole.Child),
        new(Juniper, "Juniper", MemberRole.Child),
    ];

    public static Recipe Dish(RecipeId id, Cuisine cuisine, bool forKids, params string[] ingredients) => new(
        id,
        "Invented " + id.Value.Replace('-', ' '),
        cuisine,
        ingredients.Select(name => new Ingredient(null, null, name)),
        [new RecipeStep("Cook it.", 40, true)],
        4,
        null,
        forKids);

    /// <summary>Eight cards. Every one has salt, which says nothing about anyone's taste.</summary>
    public static List<Recipe> Box() =>
    [
        Dish(Risotto, Cuisine.Italian, false, "arborio rice", "Mushrooms", "onion", "salt"),
        Dish(Pasta, Cuisine.Italian, true, "pasta", "tomatoes", "onion", "salt"),
        Dish(Curry, Cuisine.Indian, false, "lentils", "onion", "curry paste", "salt"),
        Dish(Burritos, Cuisine.Mexican, true, "black beans", "tortillas", "onion", "salt"),
        Dish(Tacos, Cuisine.Mexican, true, "corn", "tortillas", "onion", "salt"),
        Dish(Omelette, Cuisine.French, false, "eggs", "parsley", "salt"),
        Dish(Tart, Cuisine.French, false, "onion", "pastry", "eggs", "salt"),
        Dish(Chicken, Cuisine.American, true, "chicken", "potatoes", "salt"),
    ];

    public static TasteRating Rate(PersonId person, RecipeId recipe, TasteVerdict verdict, int dayOffset = 0, string? reason = null) =>
        new(person, recipe, Day(dayOffset), verdict, reason);

    public static TasteNote Note(PersonId person, TasteSubjectKind kind, string subject, TasteStance stance, TasteOrigin origin = TasteOrigin.Learned) =>
        new(person, kind, subject, stance, origin, Day(0));

    public static TasteProfile Build(IEnumerable<TasteRating>? ratings = null, IEnumerable<TasteNote>? notes = null, IReadOnlyList<Recipe>? box = null) =>
        TasteProfile.Build(People(), box ?? Box(), ratings ?? [], notes ?? []);
}

/// <summary>A fake Claude: answers each piece of evidence from a table, and counts the questions it was asked.</summary>
internal sealed class FakeTasteJudge : ITasteJudge
{
    private readonly Func<TasteEvidence, TasteJudgement> answer;

    public FakeTasteJudge(Func<TasteEvidence, TasteJudgement> answer) => this.answer = answer;

    public List<TasteEvidence> Asked { get; } = [];

    public Task<TasteJudgement> JudgeAsync(TasteEvidence evidence, CancellationToken cancellation)
    {
        Asked.Add(evidence);
        return Task.FromResult(answer(evidence));
    }
}
