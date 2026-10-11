using System.Text.RegularExpressions;
using Pulp.Domain.Children;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.People;
using Pulp.Domain.Shell;
using static Pulp.Tests.ChildView.ChildViewFixtures;

namespace Pulp.Tests.ChildView;

/// <summary>
/// A person is shown by the name the household holds for them (<c>HouseholdView.NameOf</c>), never by
/// their id. The id is the key everything is stored under and is not written for anyone to read; after a
/// rename it is not even the person's name. Invented names.
/// </summary>
public class DisplayNamesTests
{
    private static string NameOf(ViewerId id) => id == Robin ? "Robin Example" : id == Sam ? "Sam Example" : "Someone";

    [Fact]
    public void AChildNoLongerOnTheRosterIsNamedByTheirName()
    {
        var session = new ChildViewSession([Robin], PulpArea.Watching);
        var roster = new HouseholdRoster([new HouseholdViewer(Parent, IsChild: false)]);

        var result = session.Resolve(roster, [RobinRules()], NameOf);

        Assert.Equal(ChildViewText.NotAChild("Robin Example"), result.Problem);
    }

    [Fact]
    public void AChildWithNoFilterIsNamedByTheirName()
    {
        var session = new ChildViewSession([Robin], PulpArea.Watching);

        var result = session.Resolve(Roster(), [], NameOf);

        Assert.Equal(ChildViewText.NoFilter("Robin Example"), result.Problem);
    }

    [Fact]
    public void WithoutANameSourceTheIdIsReadBackAsAName()
    {
        var session = new ChildViewSession([new ViewerId("robin-vale")], PulpArea.Watching);

        var result = session.Resolve(new HouseholdRoster([new HouseholdViewer(Parent, false)]), []);

        Assert.Equal(ChildViewText.NotAChild("Robin Vale"), result.Problem);
    }

    // Source-level: no screen writes a viewer's id as their name.
    private static readonly Regex IdAsName = new(
        @"\b(?<v>id|viewer|child|who|parent|person|adult)\s*=>\s*\k<v>\.Value\b|\.Child\.Value\b|\bviewer\.Value\b|\bwho\.Value\b|\bparent\.Value\b",
        RegexOptions.CultureInvariant);

    [Fact]
    public void NoScreenWritesAViewerIdAsAName()
    {
        var head = PulpSource.Files(".cs")
            .Select(file => (Path: PulpSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Path.StartsWith("Pulp.Android/", StringComparison.Ordinal))
            .ToList();

        foreach (var screen in new[]
        {
            "MainActivity.cs", "Shell/ChildPicker.cs", "Favorites/FavoritesPage.cs", "Title/TitleActivity.cs",
            "FirstRun/FirstRunActivity.cs", "Settings/HouseholdActivity.cs",
        })
        {
            Assert.Contains(head, file => file.Path == "Pulp.Android/" + screen);
        }

        Assert.True(head.Count(file => file.Text.Contains("NameOf", StringComparison.Ordinal)) >= 5,
            "Too few screens ask the household for a name; the guard is not looking at the right files.");

        var offenders = head
            .SelectMany(file => IdAsName.Matches(file.Text).Select(match => file.Path + ": " + match.Value))
            .ToList();
        Assert.True(offenders.Count == 0, "A screen writes a ViewerId's Value where a name belongs:\n" + string.Join("\n", offenders));
    }
}
