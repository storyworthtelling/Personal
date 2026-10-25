using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Groceries;

/// <summary>What the household has on hand, and which of its standing staples are out. Immutable; every change returns a new pantry.</summary>
public sealed class Pantry
{
    private static readonly string[] DayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private readonly Dictionary<string, Staple> byKey;

    private Pantry(Dictionary<string, Staple> byKey)
    {
        this.byKey = byKey;
        Items = byKey.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToList();
    }

    public static Pantry Empty { get; } = new([]);

    /// <summary>Every item, in name order, one per ingredient (see <see cref="IngredientNames.Key"/>).</summary>
    public IReadOnlyList<Staple> Items { get; }

    /// <summary>The standing staples: what the meal sheet prints as a checklist.</summary>
    public IReadOnlyList<Staple> Standing => Items.Where(item => item.Standing).ToList();

    /// <exception cref="ArgumentException">Two items name one ingredient.</exception>
    public static Pantry From(IEnumerable<Staple> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var held = new Dictionary<string, Staple>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!held.TryAdd(IngredientNames.Key(item.Name), item))
            {
                throw new ArgumentException("Two pantry items name the same ingredient.", nameof(items));
            }
        }

        return new Pantry(held);
    }

    public Staple? Find(string ingredientName)
    {
        ArgumentNullException.ThrowIfNull(ingredientName);
        return byKey.GetValueOrDefault(IngredientNames.Key(ingredientName));
    }

    /// <summary>The pantry with <paramref name="item"/> added, or replacing the item of the same ingredient.</summary>
    public Pantry With(Staple item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var next = new Dictionary<string, Staple>(byKey, StringComparer.Ordinal) { [IngredientNames.Key(item.Name)] = item };
        return new Pantry(next);
    }

    /// <summary>
    /// After a week in which some planned dinners were not cooked: the perishables bought for them are written off (a standing
    /// staple becomes "need more", a one-off item is dropped), and their other ingredients are assumed in stock. A dinner that
    /// was cooked, or a day that was not a cooked dinner, changes nothing.
    /// </summary>
    /// <exception cref="ArgumentException">An uncooked dinner names a card that is not in the box.</exception>
    public Pantry AfterUncooked(MealWeek week, IReadOnlySet<DateOnly> uncookedDays, IReadOnlyList<Recipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(uncookedDays);
        ArgumentNullException.ThrowIfNull(recipes);

        var next = new Dictionary<string, Staple>(byKey, StringComparer.Ordinal);
        if (!week.IsPlanned)
        {
            return this;
        }

        foreach (var dinner in week.Dinners.Where(item => item.Kind == DinnerKind.Cooked && uncookedDays.Contains(item.Day)))
        {
            var card = recipes.FirstOrDefault(item => item.Id == dinner.Recipe)
                ?? throw new ArgumentException($"{DayNames[dinner.Day.DayNumber - week.WeekStart.DayNumber]}'s dinner names a card that is not in the box, so its stock could not be worked out.", nameof(recipes));
            foreach (var ingredient in card.Ingredients)
            {
                Reckon(next, ingredient.Name);
            }
        }

        return new Pantry(next);
    }

    private static void Reckon(Dictionary<string, Staple> held, string name)
    {
        var key = IngredientNames.Key(name);
        held.TryGetValue(key, out var existing);
        var aisle = existing?.Aisle ?? Aisles.Of(name);
        if (Aisles.IsPerishable(aisle))
        {
            if (existing is { Standing: true })
            {
                held[key] = existing with { NeedMore = true };
            }
            else if (existing is not null)
            {
                held.Remove(key);
            }

            return;
        }

        if (existing is null)
        {
            var shown = name.Trim();
            held[key] = new Staple(shown.Length > Staple.MaxNameLength ? shown[..Staple.MaxNameLength].TrimEnd() : shown, aisle, needMore: false, standing: false);
        }
        else if (existing.NeedMore)
        {
            held[key] = existing with { NeedMore = false };
        }
    }
}
