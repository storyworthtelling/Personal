using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Friday;
using static Pulp.Tests.Discover.DiscoverFixtures;

namespace Pulp.Tests.Discover;

/// <summary>
/// The safety net between the model's answer and Discover's results (D104's rule, Score's
/// <c>PlaylistPlan</c>): a candidate is kept only when it resolves to a real catalogue title, is on
/// the household's services and is shown to every child in the view. Anything else is dropped, so a
/// made-up title degrades to a shorter list rather than a row nobody can open.
/// </summary>
public class MoodPlanTests
{
    private static MoodQuery AdultQuery(int max = MoodQuery.DefaultMaxTitles) =>
        MoodQuery.For("rainy and quiet", DiscoverContext.For(MoodViewer.Adult(Sam), Roster, HouseholdServices, TasteProfile.Empty), max);

    private static MoodQuery ChildQuery() =>
        MoodQuery.For("funny animals", DiscoverContext.For(MoodViewer.Child(WrenRules), Roster, HouseholdServices, TasteProfile.Empty));

    private static MoodAnswer Answer(params MoodCandidate[] candidates) => MoodAnswer.Answered(string.Empty, candidates);

    private static MoodCandidate C(string name, int? year = null, TitleKind? kind = null) => new(name, year, kind);

    [Fact]
    public void ARealTitleOnOurServicesIsKeptInTheModelsOrder()
    {
        var result = MoodPlan.Resolve(AdultQuery(), Answer(C("Kettle & Crumb", 2021), C("The Glasshouse Year", 2019)),
            KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal([Kettle.Id, Glasshouse.Id], result.Titles.Select(title => title.Id));
        Assert.Equal(0, result.Dropped);
    }

    [Fact]
    public void AMadeUpTitleIsDropped()
    {
        var result = MoodPlan.Resolve(AdultQuery(), Answer(C("A Film Nobody Made", 2020), C("Nightwater", 2017)),
            KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal([Nightwater.Id], result.Titles.Select(title => title.Id));
        Assert.Equal(1, result.Unresolved);
    }

    [Fact]
    public void ATitleOffTheHouseholdsServicesIsDropped()
    {
        var result = MoodPlan.Resolve(AdultQuery(), Answer(C("Far Shore", 2018)), KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Empty(result.Titles);
        Assert.Equal(1, result.OffServices);
    }

    [Fact]
    public void NamesMatchWithoutCasePunctuationOrSpacing()
    {
        var result = MoodPlan.Resolve(AdultQuery(), Answer(C("kettle and crumb"), C("  NIGHTWATER. ")),
            KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal([Kettle.Id, Nightwater.Id], result.Titles.Select(title => title.Id));
    }

    [Fact]
    public void TheYearPicksBetweenTwoTitlesOfOneName()
    {
        var result = MoodPlan.Resolve(AdultQuery(), Answer(C("The Glasshouse Year", 2024)), KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal([GlasshouseRemake.Id], result.Titles.Select(title => title.Id));
    }

    [Fact]
    public void AYearOffByOneStillMatchesButAWrongYearDoesNot()
    {
        var near = MoodPlan.Resolve(AdultQuery(), Answer(C("Nightwater", 2018)), KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);
        var wrong = MoodPlan.Resolve(AdultQuery(), Answer(C("Nightwater", 2009)), KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal([Nightwater.Id], near.Titles.Select(title => title.Id));
        Assert.Empty(wrong.Titles);
    }

    [Fact]
    public void AnAmbiguousNameWithNoYearIsDroppedRatherThanGuessed()
    {
        var result = MoodPlan.Resolve(AdultQuery(), Answer(C("The Glasshouse Year")), KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Empty(result.Titles);
        Assert.Equal(1, result.Unresolved);
    }

    [Fact]
    public void AKindThatDisagreesIsNotAMatch()
    {
        var result = MoodPlan.Resolve(AdultQuery(), Answer(C("Nightwater", 2017, TitleKind.Show)), KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Empty(result.Titles);
    }

    [Fact]
    public void RepeatsCollapseAndTheResultIsCapped()
    {
        var result = MoodPlan.Resolve(AdultQuery(max: 2),
            Answer(C("Nightwater", 2017), C("nightwater", 2017), C("Kettle & Crumb"), C("Lantern Cove")),
            KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal([Nightwater.Id, Kettle.Id], result.Titles.Select(title => title.Id));
    }

    [Fact]
    public void NoIdeaResolvesToNothing()
    {
        var result = MoodPlan.Resolve(AdultQuery(), MoodAnswer.NoIdea(MoodOutcome.Malformed), KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Empty(result.Titles);
        Assert.Equal(MoodOutcome.Malformed, result.Outcome);
    }

    [Fact]
    public void TheSearchTermsAreEachDistinctNameOnce()
    {
        var terms = MoodPlan.SearchTerms(Answer(C("Nightwater"), C(" nightwater "), C("Lantern Cove")));

        Assert.Equal(["Nightwater", "Lantern Cove"], terms);
    }

    /// <summary>
    /// The guard: a child's mood result never holds a title the child's filter hides — above the
    /// ceiling, unrated, or in a blocked franchise — even when the model names it and the catalogue
    /// has it. It asserts the answer really did name hidden titles, so it cannot pass on an empty run.
    /// </summary>
    [Fact]
    public void AChildsMoodResultNeverHoldsATitleTheirFilterHides()
    {
        var everything = Answer(KnownTitles.Select(title => C(title.Name, title.Year, title.Kind)).ToArray());
        var hiddenNamed = KnownTitles
            .Where(title => !ChildFilter.Decide(WrenRules, DiscoverFacts.For(title, FranchisesOf)).Visible)
            .Select(title => title.Id)
            .ToList();

        var result = MoodPlan.Resolve(ChildQuery(), everything, KnownTitles, MoodViewer.Child(WrenRules), FranchisesOf);

        Assert.True(hiddenNamed.Count >= 3, "The fixture must name titles Wren's filter hides, or this guard checks nothing.");
        Assert.Contains(Nightwater.Id, hiddenNamed);
        Assert.Contains(Unrated.Id, hiddenNamed);
        Assert.Contains(Lantern.Id, hiddenNamed);
        Assert.NotEmpty(result.Titles);
        Assert.DoesNotContain(result.Titles, title => hiddenNamed.Contains(title.Id));
        Assert.True(result.Hidden >= 3, $"Only {result.Hidden} titles were hidden; the filter was not asked.");
    }

    [Fact]
    public void AnAdultSeesWhatTheChildFilterWouldHide()
    {
        var result = MoodPlan.Resolve(AdultQuery(), Answer(C("Nightwater", 2017), C("Lantern Cove", 2020)),
            KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal([Nightwater.Id, Lantern.Id], result.Titles.Select(title => title.Id));
    }

    [Fact]
    public void ASittingWithAChildGetsTheChildsFilter()
    {
        var query = MoodQuery.For("cosy", DiscoverContext.For(MoodViewer.WithChildren(Sam, [WrenRules]), Roster, HouseholdServices, TasteProfile.Empty));

        var result = MoodPlan.Resolve(query, Answer(C("Nightwater", 2017), C("Kettle & Crumb", 2021)),
            KnownTitles, MoodViewer.WithChildren(Sam, [WrenRules]), FranchisesOf);

        Assert.Equal([Kettle.Id], result.Titles.Select(title => title.Id));
    }

    [Fact]
    public void TheFactsGiveTheFilterTheTitlesStreamingHomes()
    {
        var facts = DiscoverFacts.For(Kettle, FranchisesOf);

        Assert.Equal(Kettle.Id, facts.Id);
        Assert.Equal([Sample.Id], facts.Homes);
        Assert.Equal(Kettle.Rating, facts.Rating);
    }
}
