using Constitution.Domain.Documents;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Groceries;

/// <summary>One line of the list. <see cref="Quantity"/> is the amount with its unit as it prints ("1½ cups"), empty for "to taste" and for a staple that is simply out.</summary>
public sealed record GroceryLine(string Name, string Quantity, Aisle Aisle, bool IsStaple);

/// <summary>The lines in one store section, by name.</summary>
public sealed record GrocerySection(Aisle Aisle, IReadOnlyList<GroceryLine> Lines);

/// <summary>
/// The week's shopping list: the plan's ingredients minus what is on hand, by store section (C-6.4.2).
///
/// <list type="bullet">
///   <item><b>Only cooked dinners buy anything.</b> Leftovers, eating out and an empty evening add nothing, and a week that was not planned is an empty list.</item>
///   <item><b>Amounts are scaled to the table</b> (the card's servings to the headcount, by <see cref="ServingsScale"/>'s own reading) and <b>identical ingredients are added</b>,
///   but only when they can be: the same name and the same unit. A cup and a tablespoon, an amount and a bare "salt", and anything the scaler cannot read
///   exactly (a range, "a pinch") stay on lines of their own as the card has them, rather than being guessed into one.</item>
///   <item><b>What is on hand comes off.</b> An ingredient the pantry holds (and has not marked as needed) is not listed at all; one the owner ticked
///   "need more" is listed once, as a staple. A staple that is out and not in any dinner is listed on its own, with no amount.</item>
///   <item><b>The aisle is the pantry item's own when there is one,</b> else <see cref="Aisles.Of"/>.</item>
/// </list>
/// </summary>
public sealed class GroceryList
{
    private GroceryList(IReadOnlyList<GrocerySection> sections) => Sections = sections;

    /// <summary>Only the sections that have a line, in shopping order.</summary>
    public IReadOnlyList<GrocerySection> Sections { get; }

    public IReadOnlyList<GroceryLine> Lines => Sections.SelectMany(section => section.Lines).ToList();

    public bool IsEmpty => Sections.Count == 0;

    /// <param name="week">The approved week.</param>
    /// <param name="recipes">The box, for the ingredients of each cooked dinner.</param>
    /// <param name="pantry">What is on hand and which staples are out.</param>
    /// <param name="headcount">Who is at the table; every card is scaled to it.</param>
    /// <exception cref="ArgumentOutOfRangeException">The headcount is not at least one.</exception>
    /// <exception cref="ArgumentException">A cooked dinner names a card that is not in the box.</exception>
    public static GroceryList From(MealWeek week, IReadOnlyList<Recipe> recipes, Pantry pantry, int headcount)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(pantry);
        ArgumentOutOfRangeException.ThrowIfLessThan(headcount, 1);

        var wanted = new List<Wanted>();
        if (week.IsPlanned)
        {
            foreach (var dinner in week.Dinners.Where(item => item.Kind == DinnerKind.Cooked))
            {
                var card = recipes.FirstOrDefault(item => item.Id == dinner.Recipe)
                    ?? throw new ArgumentException($"{dinner.Day.DayOfWeek}'s dinner names a card that is not in the box, so its groceries could not be listed.", nameof(recipes));
                foreach (var ingredient in card.Ingredients)
                {
                    Add(wanted, ingredient, card.Servings, headcount);
                }
            }
        }

        var lines = new List<GroceryLine>();
        foreach (var item in wanted)
        {
            var held = pantry.Find(item.Name);
            if (held is { NeedMore: false })
            {
                continue;
            }

            lines.Add(new GroceryLine(item.Name, item.Quantity(), held?.Aisle ?? Aisles.Of(item.Name), held is { Standing: true }));
        }

        var listed = lines.Select(line => IngredientNames.Key(line.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var out1 in pantry.Items.Where(item => item.NeedMore && !listed.Contains(IngredientNames.Key(item.Name))))
        {
            lines.Add(new GroceryLine(out1.Name, string.Empty, out1.Aisle, out1.Standing));
        }

        var sections = Aisles.All
            .Select(aisle => new GrocerySection(
                aisle,
                lines.Where(line => line.Aisle == aisle)
                    .OrderBy(line => IngredientNames.Key(line.Name), StringComparer.Ordinal)
                    .ThenBy(line => line.Quantity, StringComparer.Ordinal)
                    .ToList()))
            .Where(section => section.Lines.Count > 0)
            .ToList();
        return new GroceryList(sections);
    }

    private static void Add(List<Wanted> wanted, Ingredient ingredient, int? cardServings, int headcount)
    {
        var nameKey = IngredientNames.Key(ingredient.Name);
        var unitKey = Units.Key(ingredient.Unit);
        if (ingredient.Quantity is null)
        {
            var bare = wanted.FirstOrDefault(item => item.Kind == WantedKind.Bare && item.NameKey == nameKey && item.UnitKey == unitKey);
            if (bare is null)
            {
                wanted.Add(Wanted.Bare(ingredient.Name, nameKey, ingredient.Unit, unitKey));
            }

            return;
        }

        if (QuantityText.TryRead(ingredient.Quantity, out var amount))
        {
            var share = cardServings is { } servings ? new Fraction(headcount, servings) : new Fraction(1, 1);
            var counted = amount * share;
            var found = wanted.FirstOrDefault(item => item.Kind == WantedKind.Counted && item.NameKey == nameKey && item.UnitKey == unitKey);
            if (found is null)
            {
                wanted.Add(Wanted.Counted(ingredient.Name, nameKey, ingredient.Unit, unitKey, counted));
            }
            else
            {
                found.Total += counted;
            }

            return;
        }

        // Not a number the scaler reads exactly: the card's words, scaled or not exactly as the recipe page would.
        var asWritten = ServingsScale.Scale(ingredient.Quantity, cardServings ?? headcount, headcount).Text;
        wanted.Add(Wanted.Words(ingredient.Name, nameKey, ingredient.Unit, unitKey, asWritten));
    }

    private enum WantedKind
    {
        Counted,
        Bare,
        Words,
    }

    private sealed class Wanted
    {
        private readonly string? unit;
        private readonly string words = string.Empty;

        private Wanted(WantedKind kind, string name, string nameKey, string? unit, string unitKey)
        {
            Kind = kind;
            Name = name;
            NameKey = nameKey;
            this.unit = unit;
            UnitKey = unitKey;
        }

        public WantedKind Kind { get; }

        public string Name { get; }

        public string NameKey { get; }

        public string UnitKey { get; }

        public Fraction Total { get; set; }

        public static Wanted Counted(string name, string nameKey, string? unit, string unitKey, Fraction total) =>
            new(WantedKind.Counted, name, nameKey, unit, unitKey) { Total = total };

        public static Wanted Bare(string name, string nameKey, string? unit, string unitKey) => new(WantedKind.Bare, name, nameKey, unit, unitKey);

        public static Wanted Words(string name, string nameKey, string? unit, string unitKey, string quantity) =>
            new(WantedKind.Words, name, nameKey, unit, unitKey, quantity);

        private Wanted(WantedKind kind, string name, string nameKey, string? unit, string unitKey, string words)
            : this(kind, name, nameKey, unit, unitKey) => this.words = words;

        public string Quantity() => Kind switch
        {
            WantedKind.Counted => QuantityText.Write(Total, unit),
            WantedKind.Words => string.IsNullOrWhiteSpace(unit) ? words : words + " " + unit.Trim(),
            _ => string.IsNullOrWhiteSpace(unit) ? string.Empty : unit.Trim(),
        };
    }
}
