using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using Pulp.Domain.Settings;
using Storyworthtelling.Household;
using Storyworthtelling.People;

namespace Pulp.Tests.ForewordHousehold;

/// <summary>
/// Foreword owns the household; Pulp's viewers are a mirror of it, under the viewer ids Pulp's sittings, vetoes, stars and rules
/// already use, so nobody's watching history is orphaned.
/// </summary>
public class HouseholdMirrorTests
{
    private static long wall = 20_000;

    private static HlcTimestamp Next() => new(++wall, 0);

    private static HouseholdPeople Suite(params HouseholdPerson[] people)
    {
        var editor = new HouseholdPeopleEditor(canEdit: true);
        var document = HouseholdDocument.Empty;
        foreach (var person in people)
        {
            document = editor.Put(document, person, Next(), "phone-owner").Document;
        }

        return HouseholdPeople.From(document);
    }

    private static HouseholdPerson Sam => new("sam", "Sam", false, null, true, "sam@example.com", null)
    {
        Links = new Dictionary<string, string> { [PeopleApps.Pulp] = "viewer-sam" },
    };

    private static HouseholdPerson Robin => new("robin", "Robin", true, null, false, null, null);

    private static HouseholdDocument Mirror(HouseholdDocument document, HouseholdPeople suite) =>
        HouseholdMirror.Apply(document, suite, Next, "phone-owner");

    [Fact]
    public void EveryoneBecomesAViewerUnderPulpsOwnId()
    {
        var view = HouseholdView.Of(Mirror(HouseholdDocument.Empty, Suite(Sam, Robin)));

        Assert.True(view.Roster!.Contains(new ViewerId("viewer-sam")));
        Assert.True(view.Roster.IsChild(new ViewerId("robin")));
        Assert.Equal("Sam", view.NameOf(new ViewerId("viewer-sam")));
    }

    [Fact]
    public void AChildArrivesWithTheStrictestRules()
    {
        var view = HouseholdView.Of(Mirror(HouseholdDocument.Empty, Suite(Sam, Robin)));

        Assert.Equal(AgeBand.UnderSeven, view.ChildRules.Single(rules => rules.Child == new ViewerId("robin")).Ceiling);
    }

    [Fact]
    public void AnAdultsGoogleAddressIsTheirAccount()
    {
        var view = HouseholdView.Of(Mirror(HouseholdDocument.Empty, Suite(Sam, Robin)));

        Assert.Equal(["sam@example.com"], view.AdultGoogleAccounts);
    }

    [Fact]
    public void AChildsRulesSurviveARename()
    {
        var document = Mirror(HouseholdDocument.Empty, Suite(Sam, Robin));
        var tighter = ChildRuleEdits.Ceiling(HouseholdView.Of(document).ChildRules.Single(), AgeBand.SevenToNine);
        document = HouseholdRecords.Put(document, HouseholdMaps.ChildRules, tighter, Next(), "phone-owner");

        document = Mirror(document, Suite(Sam, Robin with { Name = "Juniper" }));

        var view = HouseholdView.Of(document);
        Assert.Equal("Juniper", view.NameOf(new ViewerId("robin")));
        Assert.Equal(AgeBand.SevenToNine, view.ChildRules.Single().Ceiling);
    }

    [Fact]
    public void GrowingUpDropsTheRules()
    {
        var document = Mirror(HouseholdDocument.Empty, Suite(Sam, Robin));

        document = Mirror(document, Suite(Sam, Robin with { IsChild = false }));

        var view = HouseholdView.Of(document);
        Assert.False(view.Roster!.IsChild(new ViewerId("robin")));
        Assert.Empty(view.ChildRules);
    }

    [Fact]
    public void SomeoneWhoLeftIsNoLongerAViewer()
    {
        var document = Mirror(HouseholdDocument.Empty, Suite(Sam, Robin));

        document = Mirror(document, Suite(Sam));

        var view = HouseholdView.Of(document);
        Assert.False(view.Roster!.Contains(new ViewerId("robin")));
        Assert.Empty(view.ChildRules);
    }

    [Fact]
    public void AnUnchangedHouseholdWritesNothing()
    {
        var suite = Suite(Sam, Robin);
        var document = Mirror(HouseholdDocument.Empty, suite);

        Assert.Same(document, Mirror(document, suite));
    }

    [Fact]
    public void AnEmptyHouseholdLeavesTheViewersAlone()
    {
        var document = Mirror(HouseholdDocument.Empty, Suite(Sam));

        Assert.Same(document, Mirror(document, HouseholdPeople.Empty));
    }

    [Fact]
    public void ThePeoplePulpHadAreOfferedWithTheirIdsAndAccounts()
    {
        var document = PersonEdits.ApplyAll(HouseholdDocument.Empty,
            PersonEdits.Add(new HouseholdViewer(new ViewerId("v-1"), false), new ViewerNameEntry(new ViewerId("v-1"), "Lee"), null), Next(), "phone-owner");
        document = PersonEdits.ApplyAll(document,
            PersonEdits.Add(new HouseholdViewer(new ViewerId("v-2"), true), new ViewerNameEntry(new ViewerId("v-2"), "Robin"), ChildRuleEdits.NewChild(new ViewerId("v-2"))), Next(), "phone-owner");
        document = HouseholdRecords.Put(document, HouseholdMaps.AdultAccount, new AdultAccountEntry(new ViewerId("v-1"), "lee@example.com"), Next(), "phone-owner");

        var offered = HouseholdMirror.ForImport(HouseholdView.Of(document));

        var lee = offered.Single(person => person.Name == "Lee");
        Assert.Equal("v-1", lee.LocalId);
        Assert.Equal("lee@example.com", lee.GoogleAddress);
        Assert.True(offered.Single(person => person.Name == "Robin").IsChild);
    }
}
