using Constitution.Domain.Documents;
using Constitution.Domain.Groceries;
using static Constitution.Tests.Groceries.GroceryData;

namespace Constitution.Tests.Groceries;

/// <summary>Seeded properties of the list: merging adds exactly, and reading a quantity agrees with the scaler the recipe page uses.</summary>
public class GroceryPropertyTests
{
    private static readonly (string Text, decimal Value)[] Eighths =
    [
        ("1", 1m), ("2", 2m), ("3", 3m), ("½", 0.5m), ("¼", 0.25m), ("¾", 0.75m), ("⅛", 0.125m), ("⅜", 0.375m),
        ("1/2", 0.5m), ("3/4", 0.75m), ("1 1/2", 1.5m), ("1½", 1.5m), ("2 ¼", 2.25m), ("0.125", 0.125m), (".5", 0.5m), ("2.75", 2.75m),
    ];

    private static readonly Dictionary<char, decimal> Glyphs = new()
    {
        ['⅛'] = 0.125m, ['¼'] = 0.25m, ['⅜'] = 0.375m, ['½'] = 0.5m, ['⅝'] = 0.625m, ['¾'] = 0.75m, ['⅞'] = 0.875m,
    };

    /// <summary>Reads what the printer writes for an eighth: whole digits, then at most one glyph, then the unit.</summary>
    private static decimal ValueOfPrinted(string quantity)
    {
        var number = quantity.Split(' ')[0];
        var digits = new string(number.TakeWhile(char.IsAsciiDigit).ToArray());
        var value = digits.Length == 0 ? 0m : decimal.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        var rest = number[digits.Length..];
        if (rest.Length == 1 && Glyphs.TryGetValue(rest[0], out var glyph))
        {
            value += glyph;
        }
        else
        {
            Assert.Equal(string.Empty, rest);
        }

        return value;
    }

    [Fact]
    public void MergedQuantitiesOfAddableUnitsEqualTheSumOfTheParts()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var random = new Random(seed);
            var count = random.Next(1, 7);
            var cards = new List<Constitution.Domain.Recipes.Recipe>();
            var expected = 0m;
            var doubled = random.Next(2) == 0;
            for (var index = 0; index < count; index++)
            {
                var (text, value) = Eighths[random.Next(Eighths.Length)];
                expected += value;
                cards.Add(Card("dish-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), "Dish " + index.ToString(System.Globalization.CultureInfo.InvariantCulture), 2, Item(text, "cups", "rolled oats")));
            }

            // Two servings a card; four at the table doubles every part.
            var headcount = doubled ? 4 : 2;
            if (doubled)
            {
                expected *= 2;
            }

            var week = Cooking(cards.Select((card, day) => (day, card)).ToArray());
            var list = GroceryList.From(week, cards, Pantry.Empty, headcount);

            var line = Assert.Single(list.Lines);
            Assert.True(expected == ValueOfPrinted(line.Quantity), $"seed {seed}: expected {expected} but the list says '{line.Quantity}'");
        }
    }

    [Fact]
    public void ReadingAQuantityAgreesWithTheServingsScaler()
    {
        string[] samples =
        [
            "1", "2", "12", "½", "¾", "1½", "1 ½", "1 1/2", "3/4", "0.75", ".5", "2.25", "⅓", "1⅓", "2 3/4", "0.333",
            "⅔", "⅞", "⅝", "2⅜", "1-2", "1 to 2", "a pinch", "a handful", "1 (14 oz)", "1/0", "0", "abc", "", "  ", "10 000",
        ];

        foreach (var sample in samples)
        {
            var card = Card("sample", "Sample", 1, Item(sample, null, "mystery"));

            var list = GroceryList.From(Cooking((0, card)), [card], Pantry.Empty, 2);

            var expected = ServingsScale.Scale(sample, 1, 2).Text;
            Assert.True(expected == Assert.Single(list.Lines).Quantity, $"'{sample}': the scaler writes '{expected}'");
        }
    }
}
