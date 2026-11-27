using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// <b>Foreword owns the household</b> (owner's ruling, Nov 2026). Constitution and Pulp once brought their own people into it on every
/// round, which put back everyone Foreword removed and added a copy of anyone whose name was already linked: four people set in
/// Foreword came back as sixteen. Every other app opens the household file as a reader and never imports, fills the rhythm, puts,
/// removes or links a person.
/// </summary>
public class OnlyForewordWritesTheHouseholdTests
{
    private static IEnumerable<(string Relative, string Text)> Heads() =>
        Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(path => (Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/'), path))
            .Where(file => !file.Item1.StartsWith("Storyworthtelling.People/", StringComparison.Ordinal))
            .Select(file => (file.Item1, File.ReadAllText(file.path)));

    [Fact]
    public void OnlyForewordOpensTheHouseholdAsItsOwner()
    {
        var found = Heads()
            .Where(file => file.Text.Contains("PeopleFileAccess.Owner", StringComparison.Ordinal))
            .Select(file => file.Relative)
            .Where(relative => !relative.StartsWith("Foreword/", StringComparison.Ordinal))
            .ToList();

        Assert.True(found.Count == 0, "Only Foreword owns the household; open it with PeopleFileAccess.Reader:\n" + string.Join("\n", found));
    }

    [Fact]
    public void NoOtherAppEditsTheHousehold()
    {
        var edit = new Regex(@"editor\.(Import|FillRhythm|SetRhythm|Put|Remove|Link)\(", RegexOptions.Compiled);
        var found = Heads()
            .Where(file => !file.Relative.StartsWith("Foreword/", StringComparison.Ordinal))
            .Where(file => file.Text.Contains("Storyworthtelling.People", StringComparison.Ordinal) && edit.IsMatch(file.Text))
            .Select(file => file.Relative)
            .ToList();

        Assert.True(found.Count == 0, "Only Foreword edits the household; read it here:\n" + string.Join("\n", found));
    }

    [Fact]
    public void EveryPeopleFileSaysWhoOwnsIt()
    {
        var built = Heads().Count(file => file.Text.Contains("HouseholdPeopleFile(", StringComparison.Ordinal) && file.Text.Contains("new ", StringComparison.Ordinal)
            && Regex.IsMatch(file.Text, @"new (Storyworthtelling\.People\.)?HouseholdPeopleFile\("));
        var said = Heads().Count(file => Regex.IsMatch(file.Text, @"PeopleFileAccess\.(Owner|Reader)\)"));

        Assert.True(built >= 3 && said >= built, $"{built} people files built, {said} say whose they are.");
    }
}
