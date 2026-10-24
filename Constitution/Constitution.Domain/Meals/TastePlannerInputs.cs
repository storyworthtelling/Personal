using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>
/// What the committed dinner planner can be given from the taste profile, without editing the planner. Two things:
/// <see cref="NeverAgain"/>, which <see cref="MealWeekRequest.NeverAgain"/> already accepts (a card and the people, by
/// roster name, who said it), and <see cref="Appetite"/>, a ranking the planner has <b>no input for yet</b> — it is held
/// here, with <see cref="Ranked"/>, for the day the request grows one.
/// </summary>
public sealed class TastePlannerInputs
{
    private TastePlannerInputs(IReadOnlyDictionary<RecipeId, IReadOnlySet<string>> neverAgain, IReadOnlyDictionary<RecipeId, decimal> appetite)
    {
        NeverAgain = neverAgain;
        Appetite = appetite;
    }

    /// <summary>A card and the household members, by name, whose current rating of it is Never again.</summary>
    public IReadOnlyDictionary<RecipeId, IReadOnlySet<string>> NeverAgain { get; }

    /// <summary>How much the household as a whole is likely to want each card: the sum over everyone's appetite.</summary>
    public IReadOnlyDictionary<RecipeId, decimal> Appetite { get; }

    public static TastePlannerInputs From(TasteProfile profile, IReadOnlyList<TastePerson> people, IReadOnlyList<Recipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(recipes);
        var names = people.ToDictionary(person => person.Id, person => person.Name);
        var never = new Dictionary<RecipeId, HashSet<string>>();
        foreach (var taste in profile.People.Where(taste => names.ContainsKey(taste.Person)))
        {
            foreach (var recipe in taste.NeverAgain)
            {
                if (!never.TryGetValue(recipe, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    never[recipe] = set;
                }

                set.Add(names[taste.Person]);
            }
        }

        var appetite = recipes.ToDictionary(recipe => recipe.Id, recipe => profile.People.Sum(taste => taste.Appetite(recipe)));
        return new TastePlannerInputs(never.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value), appetite);
    }

    /// <summary>
    /// The request with these never-agains added to its own (a union, names compared without case). The request is not
    /// changed; with nothing to add the same request comes back.
    /// </summary>
    public MealWeekRequest Apply(MealWeekRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (NeverAgain.Count == 0)
        {
            return request;
        }

        var merged = new Dictionary<RecipeId, IReadOnlySet<string>>();
        foreach (var (recipe, people) in request.NeverAgain)
        {
            merged[recipe] = new HashSet<string>(people, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var (recipe, people) in NeverAgain)
        {
            if (merged.TryGetValue(recipe, out var existing))
            {
                ((HashSet<string>)existing).UnionWith(people);
            }
            else
            {
                merged[recipe] = new HashSet<string>(people, StringComparer.OrdinalIgnoreCase);
            }
        }

        return request with { NeverAgain = merged };
    }

    /// <summary>The cards best first by household appetite, ties by id. A card the profile never saw scores zero.</summary>
    public IReadOnlyList<Recipe> Ranked(IEnumerable<Recipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        return recipes
            .OrderByDescending(recipe => Appetite.GetValueOrDefault(recipe.Id))
            .ThenBy(recipe => recipe.Id.Value, StringComparer.Ordinal)
            .ToList();
    }
}
