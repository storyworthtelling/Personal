using Constitution.Domain.Groceries;
using static Constitution.Tests.Groceries.GroceryData;

namespace Constitution.Tests.Groceries;

/// <summary>C-10.1.4: the list goes to Keep as plain text, aimed at Keep alone. Every dish and figure is invented.</summary>
public class GroceryShareTests
{
    private static GroceryList Three() => GroceryList.From(ThreeDinners(), Cards, Pantry.Empty, 4);

    private static GroceryList None() => GroceryList.From(MealPlacementEmpty(), Cards, Pantry.Empty, 4);

    private static Constitution.Domain.Meals.MealWeek MealPlacementEmpty() =>
        Constitution.Tests.MealPlacement.MealPlacementData.WeekOf();

    [Fact]
    public void ASendIsAimedAtKeepAlone()
    {
        var decision = GroceryShare.Plan(Three(), keepIsInstalled: true);

        Assert.Equal(GroceryShareOutcome.Send, decision.Outcome);
        Assert.Equal("com.google.android.keep", decision.Package);
    }

    [Fact]
    public void EverySectionIsAHeaderOverItsLines()
    {
        var list = Three();
        var lines = GroceryShare.Plan(list, true).Body.Split('\n');

        foreach (var section in list.Sections)
        {
            var at = Array.IndexOf(lines, Aisles.Label(section.Aisle));
            Assert.True(at >= 0, section.Aisle + " has no header");
            for (var i = 0; i < section.Lines.Count; i++)
            {
                var line = section.Lines[i];
                var expected = line.Quantity.Length == 0 ? line.Name : line.Quantity + " " + line.Name;
                Assert.Equal(expected, lines[at + 1 + i]);
            }
        }
    }

    [Fact]
    public void SectionsComeInShoppingOrder()
    {
        var body = GroceryShare.Plan(Three(), true).Body;
        var order = Three().Sections.Select(section => body.IndexOf(Aisles.Label(section.Aisle), StringComparison.Ordinal)).ToList();

        Assert.True(order.Count > 1);
        Assert.Equal(order.OrderBy(position => position), order);
    }

    [Fact]
    public void ABareStapleIsJustItsName()
    {
        var pantry = Pantry.From([new Staple("paper towels", Aisle.Other, true, true)]);
        var body = GroceryShare.Plan(GroceryList.From(MealPlacementEmpty(), Cards, pantry, 4), true).Body;

        Assert.Contains("\npaper towels", "\n" + body);
        Assert.DoesNotContain("  ", body);
    }

    [Fact]
    public void TheBodyCarriesNoMoneyOrPeople()
    {
        var body = GroceryShare.Plan(Three(), true).Body;

        Assert.DoesNotContain("$", body);
        Assert.DoesNotContain("budget", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", body);
    }

    [Fact]
    public void WithoutKeepNothingIsSentAndNoChooserIsOffered()
    {
        var decision = GroceryShare.Plan(Three(), keepIsInstalled: false);

        Assert.Equal(GroceryShareOutcome.KeepNotInstalled, decision.Outcome);
        Assert.Equal(string.Empty, decision.Body);
        Assert.Contains("Keep", decision.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyListSendsNothing()
    {
        var decision = GroceryShare.Plan(None(), keepIsInstalled: true);

        Assert.Equal(GroceryShareOutcome.NothingToSend, decision.Outcome);
        Assert.Equal(string.Empty, decision.Body);
    }

    [Fact]
    public void TheTitleIsPlain()
    {
        Assert.Equal("Groceries", GroceryShare.Plan(Three(), true).Title);
    }

    [Fact]
    public void KeepIsTheSamePackageFlyleafAllows()
    {
        var root = Path.GetDirectoryName(ConstitutionSource.Root())!;
        var policy = File.ReadAllText(Path.Combine(root, "FocusPhone", "Codex.Domain", "Policy", "PolicyContext.cs"));

        Assert.Contains($"KeepPackage = \"{GroceryShare.KeepPackage}\"", policy);
    }
}
