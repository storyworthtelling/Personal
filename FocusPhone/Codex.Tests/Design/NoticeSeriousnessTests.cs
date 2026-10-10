using System.Text.RegularExpressions;
using Codex.Domain.Design;
using Xunit;

namespace Codex.Tests.Design;

/// <summary>
/// D291 (Pulp P12, mockup 12): the dog-ear takes the colour of the MOST SERIOUS waiting notice, not
/// the newest. The Waiting page behind it still runs newest first — only the corner's choice moved.
///
/// <para>The order is Problem, Caution, Action, Progress, Reference, Resolved: something broken, then
/// something going stale, then a decision waiting on the owner, then something waiting that is not
/// wrong, then something only worth knowing, then something already settled. Ties go to the newest.</para>
/// </summary>
public class NoticeSeriousnessTests
{
    private sealed record Waiting(string Name, FoldRole? Role);

    private static string Corner(params Waiting[] newestFirst)
    {
        var index = NoticeSeriousness.MostSerious(newestFirst, waiting => waiting.Role);
        return newestFirst[index].Name;
    }

    [Fact]
    public void TheOrderIsProblemCautionActionProgressReferenceResolved()
    {
        Assert.Equal(
            [FoldRole.Problem, FoldRole.Caution, FoldRole.Action, FoldRole.Progress, FoldRole.Reference, FoldRole.Resolved],
            NoticeSeriousness.MostSeriousFirst);
    }

    [Fact]
    public void EveryRoleIsRankedExactlyOnce()
    {
        var roles = Enum.GetValues<FoldRole>();
        Assert.NotEmpty(roles);
        Assert.Equal(roles.Length, NoticeSeriousness.MostSeriousFirst.Count);
        Assert.Equal(roles.OrderBy(role => role), NoticeSeriousness.MostSeriousFirst.OrderBy(role => role));
    }

    [Fact]
    public void ProblemOutranksEveryOtherRole()
    {
        foreach (var other in Enum.GetValues<FoldRole>().Where(role => role != FoldRole.Problem))
        {
            Assert.True(
                NoticeSeriousness.Rank(FoldRole.Problem) < NoticeSeriousness.Rank(other),
                $"Problem should outrank {other}.");
        }
    }

    [Fact]
    public void TheCornerTakesAnOlderProblemOverANewerReference()
    {
        Assert.Equal("broken key", Corner(new("handed off", FoldRole.Reference), new("broken key", FoldRole.Problem)));
    }

    [Fact]
    public void CautionOutranksProgressWhicheverIsNewer()
    {
        Assert.Equal("stale", Corner(new("uninstalling", FoldRole.Progress), new("stale", FoldRole.Caution)));
        Assert.Equal("stale", Corner(new("stale", FoldRole.Caution), new("uninstalling", FoldRole.Progress)));
    }

    [Fact]
    public void ATieGoesToTheNewest()
    {
        Assert.Equal("newer", Corner(new("newer", FoldRole.Problem), new("older", FoldRole.Problem), new("trial", FoldRole.Reference)));
    }

    [Fact]
    public void AColourThatIsNoRoleRanksBelowEveryRole()
    {
        Assert.Equal("settled", Corner(new("identity", null), new("settled", FoldRole.Resolved)));
        Assert.Equal(NoticeSeriousness.MostSeriousFirst.Count, NoticeSeriousness.Rank(null));
    }

    [Fact]
    public void ALoneNoticeWithNoRoleStillTakesTheCorner()
    {
        Assert.Equal("identity", Corner(new Waiting("identity", null)));
    }

    [Fact]
    public void NothingWaitingIsNoIndex()
    {
        Assert.Equal(-1, NoticeSeriousness.MostSerious(Array.Empty<Waiting>(), waiting => waiting.Role));
    }

    /// <summary>The native roles are the kit's <c>DogEarHue</c>, by the same names.</summary>
    [Fact]
    public void TheRolesAreTheKitsHuesByName()
    {
        var kit = Regex.Match(
            File.ReadAllText(RepoFile("Storyworthtelling.Maui/State/DogEarState.cs")),
            @"enum DogEarHue\s*\{(?<body>.*?)\}", RegexOptions.Singleline).Groups["body"].Value;
        var names = Regex.Matches(kit, @"^\s*(?<n>[A-Z]\w+),", RegexOptions.Multiline).Select(m => m.Groups["n"].Value).ToList();

        Assert.NotEmpty(names);
        Assert.Equal(names.Order(), Enum.GetNames<FoldRole>().Order());
    }

    /// <summary>
    /// The native fold uses the decision: it names the most serious notice rather than the first in
    /// the newest-first list, and every role's colour can be read back to its role.
    /// </summary>
    [Fact]
    public void TheNativeFoldTakesTheMostSeriousNotice()
    {
        var source = File.ReadAllText(RepoFile("FocusPhone/Codex.Ui.Android/DogEar.cs"));

        Assert.Contains("NoticeSeriousness.MostSerious(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("notices[0].Hue", source, StringComparison.Ordinal);

        var roles = Enum.GetNames<FoldRole>();
        Assert.NotEmpty(roles);
        foreach (var role in roles)
        {
            Assert.True(
                Regex.IsMatch(source, $@"\(FoldRole\.{role},\s*{role}\)"),
                $"DogEar.Hues.RoleOf cannot read the {role} colour back to FoldRole.{role}.");
        }
    }

    private static string RepoFile(string relative) =>
        Path.Combine(Directory.GetParent(DesignTestHelpers.FocusPhoneRoot())!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
}
