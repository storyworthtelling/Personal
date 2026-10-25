using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;

namespace Constitution.Tests.MealSheet;

/// <summary>C-2.4.3: the expected document reads the staples too. A tick is a need, a blank is none, a written word is a new staple.</summary>
public class MealSheetGroceryReadBackTests
{
    private static ExpectedDocument Expected(MealSheetModel? model = null) =>
        ExpectedDocument.ForMealSheet(model ?? MealSheetGrocerySamples.Model(), "Sam");

    [Fact]
    public void TheGroceryPageIsRead()
    {
        var document = Expected();

        Assert.Equal([1, 2, 3, 4], document.Pages.Select(p => p.Number));
        var page = document.Pages[3];
        Assert.Equal(10, page.Rules.Count);
        Assert.All(page.Rules, rule => Assert.IsType<StapleRule>(rule));
    }

    [Fact]
    public void EveryRuleNamesATargetThePageHas()
    {
        var layouts = MealSheetGrocerySamples.Pages();
        var page = Expected().Pages[3];

        foreach (var rule in page.Rules.Cast<StapleRule>())
        {
            Assert.NotNull(layouts[3].Map.TryGet(rule.LineId));
        }

        Assert.Equal(10, page.AskedTargets.Count);
    }

    [Fact]
    public void SubjectsNameTheStaple()
    {
        var rules = Expected().Pages[3].Rules.Cast<StapleRule>().ToList();

        Assert.Equal("Garlic", rules[0].Subject);
        Assert.Equal("staple-produce-1", rules[0].LineId);
        Assert.Equal("A new staple, on line 2", rules[9].Subject);
        Assert.Equal("staple-new-2", rules[9].LineId);
    }

    [Fact]
    public void TheDinnersAreUntouchedByTheNewPages()
    {
        var without = ExpectedDocument.ForMealSheet(MealSheetSamples.Model(daily: MealSheetSamples.Daily()), "Sam");
        var with = Expected();

        Assert.Equal(without.Pages.Take(3).Select(p => p.Rules.Count), with.Pages.Take(3).Select(p => p.Rules.Count));
    }

    [Fact]
    public void ContinuationPagesCarryTheirOwnStaplesOnce()
    {
        var model = MealSheetGrocerySamples.Model(MealSheetGrocerySamples.Groceries(pantry: MealSheetGrocerySamples.ManyStaples(46)));
        var document = Expected(model);
        var ids = document.Pages.Skip(3).SelectMany(p => p.Rules.Cast<StapleRule>()).Select(r => r.LineId).ToList();

        Assert.True(document.Pages.Count > 4);
        Assert.Equal(46 + 2, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void AnUntouchedPageNeedsNothing()
    {
        var page = Expected().Pages[3];
        var resolver = new InkResolver(page.Readout);
        var reads = page.Readout.Map.Targets.Select(t => Constitution.Tests.Ink.InkFixture.Empty(t.Id)).ToList();

        var resolved = resolver.Resolve(InkPageRead.Read(reads), page.Rules, MealSheetSamples.Deadline);

        Assert.Equal(10, resolved.Count);
        Assert.All(resolved, r => Assert.IsType<InkResolution.NoNeed>(r));
    }
}
