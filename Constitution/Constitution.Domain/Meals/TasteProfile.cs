using System.Globalization;
using Constitution.Domain.Household;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>Who the profile is about, as the roster names them. Carries no birthdate: the profile has no use for one.</summary>
public sealed record TastePerson(PersonId Id, string Name, MemberRole Role)
{
    public static TastePerson From(RosterEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new TastePerson(entry.Id, entry.Name, entry.Role);
    }
}

/// <summary>
/// How much each signal counts. These are the agent's first guesses, named so one edit moves them together; nothing
/// here has met a real household's ratings.
/// </summary>
public static class TasteWeights
{
    public const int Loved = 10;
    public const int Fine = 3;
    public const int NeverAgain = -10;

    /// <summary>A lasting note (seeded or learned): a like is plus this, a dislike minus it.</summary>
    public const int Note = 3;

    /// <summary>
    /// A cuisine's score counts for an unrated card only by this share: one Never again to a dish must not sink every
    /// dish of its cuisine as far as the dish itself.
    /// </summary>
    public const int CuisineShare = 3;

    /// <summary>An ingredient is only a pattern once this many rated dishes hold it.</summary>
    public const int MinimumDishes = 2;

    public static int Of(TasteVerdict verdict) => verdict switch
    {
        TasteVerdict.Loved => Loved,
        TasteVerdict.Fine => Fine,
        TasteVerdict.NeverAgain => NeverAgain,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Not a verdict this build knows."),
    };
}

/// <summary>
/// One person's derived taste. Plain numbers and ids, keyed by cuisine word, ingredient key and card id; nothing here
/// is a sentence a page could print, and <see cref="ToString"/> names nothing. The ingredient keys are the
/// comparison form of <see cref="TasteTokens"/> (lower case, singular).
/// </summary>
public sealed record PersonTaste(
    PersonId Person,
    bool IsChild,
    IReadOnlyList<RecipeId> Loved,
    IReadOnlyList<RecipeId> Fine,
    IReadOnlyList<RecipeId> NeverAgain,
    IReadOnlyDictionary<Cuisine, int> Cuisines,
    IReadOnlyDictionary<string, decimal> Ingredients,
    IReadOnlyDictionary<RecipeId, int> Dishes)
{
    /// <summary>
    /// How much this person is likely to want the card. A card they have rated is its rating and nothing else; one
    /// they have not is a share of its cuisine's score, plus each ingredient pattern or note it holds, plus a note about the dish itself.
    /// Zero for a card the profile knows nothing about.
    /// </summary>
    public decimal Appetite(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (Loved.Contains(recipe.Id))
        {
            return TasteWeights.Loved;
        }

        if (Fine.Contains(recipe.Id))
        {
            return TasteWeights.Fine;
        }

        if (NeverAgain.Contains(recipe.Id))
        {
            return TasteWeights.NeverAgain;
        }

        var lines = recipe.Ingredients.Select(ingredient => TasteTokens.Of(ingredient.Name)).ToList();
        var score = ((decimal)Cuisines.GetValueOrDefault(recipe.Cuisine) / TasteWeights.CuisineShare) + Dishes.GetValueOrDefault(recipe.Id);
        foreach (var (key, value) in Ingredients)
        {
            var needle = key.Split(' ');
            if (lines.Any(line => TasteTokens.Contains(line, needle)))
            {
                score += value;
            }
        }

        return score;
    }

    public override string ToString() => nameof(PersonTaste);
}

/// <summary>
/// The invisible taste profile (C-6.3.2): everything the household's ratings, seed dislikes and lasting notes add up to,
/// per person. It is built, kept and read by the planner's adapter; <b>nothing shows it</b> — it has no member that
/// returns words, and nothing outside the Taste files names it.
/// </summary>
public sealed record TasteProfile(IReadOnlyList<PersonTaste> People, IReadOnlyList<RecipeId> KidFriendly)
{
    public static TasteProfile Empty { get; } = new([], []);

    /// <summary>
    /// Builds the profile. Ratings are one per (person, card, day) with the last one given winning; a person's current
    /// verdict on a card is their latest day's. Ratings from someone not in <paramref name="people"/> are ignored.
    /// Nothing is invented: a person with no ratings and no notes has an empty taste.
    /// </summary>
    public static TasteProfile Build(
        IReadOnlyList<TastePerson> people,
        IReadOnlyList<Recipe> recipes,
        IEnumerable<TasteRating> ratings,
        IEnumerable<TasteNote> notes)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(ratings);
        ArgumentNullException.ThrowIfNull(notes);
        var ratingList = ratings.ToList();
        var noteList = notes.ToList();
        var built = people
            .OrderBy(person => person.Id.Value, StringComparer.Ordinal)
            .Select(person => BuildOne(person, recipes, ratingList.Where(rating => rating.Person == person.Id), noteList.Where(note => note.Person == person.Id)))
            .ToList();

        var children = built.Where(taste => taste.IsChild).ToList();
        var took = children.SelectMany(taste => taste.Loved.Concat(taste.Fine)).Distinct();
        var refused = children.SelectMany(taste => taste.NeverAgain).ToHashSet();
        var kidFriendly = took.Where(id => !refused.Contains(id)).OrderBy(id => id.Value, StringComparer.Ordinal).ToList();
        return new TasteProfile(built, kidFriendly);
    }

    public PersonTaste? For(PersonId person) => People.FirstOrDefault(taste => taste.Person == person);

    public override string ToString() => nameof(TasteProfile);

    private static PersonTaste BuildOne(TastePerson person, IReadOnlyList<Recipe> recipes, IEnumerable<TasteRating> ratings, IEnumerable<TasteNote> notes)
    {
        var byDay = new Dictionary<(RecipeId Recipe, DateOnly Day), TasteVerdict>();
        foreach (var rating in ratings)
        {
            byDay[(rating.Recipe, rating.Day)] = rating.Verdict;
        }

        var current = byDay
            .GroupBy(pair => pair.Key.Recipe)
            .ToDictionary(group => group.Key, group => group.OrderBy(pair => pair.Key.Day).Last().Value);
        var byId = recipes.ToDictionary(recipe => recipe.Id);

        List<RecipeId> With(TasteVerdict verdict) => current
            .Where(pair => pair.Value == verdict)
            .Select(pair => pair.Key)
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToList();

        var cuisines = new Dictionary<Cuisine, int>();
        foreach (var (id, verdict) in current)
        {
            if (byId.TryGetValue(id, out var recipe))
            {
                cuisines[recipe.Cuisine] = cuisines.GetValueOrDefault(recipe.Cuisine) + TasteWeights.Of(verdict);
            }
        }

        var ingredients = PatternsFrom(current, byId);
        var dishes = new Dictionary<RecipeId, int>();
        foreach (var note in notes)
        {
            var weight = note.Stance == TasteStance.Likes ? TasteWeights.Note : -TasteWeights.Note;
            switch (note.Kind)
            {
                case TasteSubjectKind.Cuisine:
                    Cuisines.Words.TryValue(note.Subject, out var cuisine);
                    cuisines[cuisine] = cuisines.GetValueOrDefault(cuisine) + weight;
                    break;
                case TasteSubjectKind.Ingredient:
                    var key = TasteTokens.Key(note.Subject);
                    ingredients[key] = ingredients.GetValueOrDefault(key) + weight;
                    break;
                case TasteSubjectKind.Dish:
                    var dish = new RecipeId(note.Subject);
                    dishes[dish] = dishes.GetValueOrDefault(dish) + weight;
                    break;
            }
        }

        return new PersonTaste(
            person.Id,
            person.Role == MemberRole.Child,
            With(TasteVerdict.Loved),
            With(TasteVerdict.Fine),
            With(TasteVerdict.NeverAgain),
            cuisines.Where(pair => pair.Value != 0).OrderBy(pair => Cuisines.Words.Word(pair.Key), StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value),
            ingredients.Where(pair => pair.Value != 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value),
            dishes.Where(pair => pair.Value != 0).OrderBy(pair => pair.Key.Value, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    /// <summary>
    /// An ingredient word held by at least <see cref="TasteWeights.MinimumDishes"/> rated dishes scores how far those
    /// dishes' average rating sits from the average of everything the person rated. Measured against that average, a
    /// word in every dish (salt) scores nothing, which is the point.
    /// </summary>
    private static Dictionary<string, decimal> PatternsFrom(Dictionary<RecipeId, TasteVerdict> current, Dictionary<RecipeId, Recipe> byId)
    {
        var scored = current
            .Where(pair => byId.ContainsKey(pair.Key))
            .Select(pair => (Words: byId[pair.Key].Ingredients.SelectMany(line => TasteTokens.Of(line.Name)).ToHashSet(StringComparer.Ordinal), Weight: (decimal)TasteWeights.Of(pair.Value)))
            .ToList();
        var patterns = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (scored.Count == 0)
        {
            return patterns;
        }

        var overall = scored.Average(item => item.Weight);
        foreach (var word in scored.SelectMany(item => item.Words).Distinct(StringComparer.Ordinal))
        {
            var holding = scored.Where(item => item.Words.Contains(word)).ToList();
            if (holding.Count >= TasteWeights.MinimumDishes)
            {
                var pattern = Math.Round(holding.Average(item => item.Weight) - overall, 2, MidpointRounding.AwayFromZero);
                if (pattern != 0)
                {
                    patterns[word] = pattern;
                }
            }
        }

        return patterns;
    }
}
