using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Design;

/// <summary>
/// Reads DESIGN.md by content so an approved Constitution departure is recorded
/// where a later guard's author will look before "fixing" it.
/// </summary>
public class DesignDocTests
{
    private static string Section(string number)
    {
        var path = Path.Combine(DesignTestHelpers.FocusPhoneRoot(), "..", "DESIGN.md");
        Assert.True(File.Exists(path), $"Missing {path}");
        var text = File.ReadAllText(path);
        var match = Regex.Match(text, $@"^## {number}\. .*?(?=^## \d+\. |\z)", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(match.Success, $"DESIGN.md has no section {number}");
        return match.Value;
    }

    [Fact]
    public void ConstitutionDeparturesAreRecorded()
    {
        var nine = Section("9");
        Assert.Contains("D45", nine);
        Assert.Contains("deep inactive cloths", nine);
        Assert.Contains("D84", nine);
        Assert.Contains("shared hues", nine);
        Assert.Contains("Constitution", nine);

        var two = Section("2");
        Assert.Contains("Scrapbook materials", two);
        Assert.Contains("scoped departure", two);
        Assert.Contains("Constitution", two);
        Assert.Contains("crinkled ground", two);
        Assert.Contains("tape", two);
        Assert.Contains("no shadows", two);
        Assert.Contains("Flyleaf", two);

        var nineteen = Section("19");
        Assert.Contains("MaterialTokens", nineteen);
        Assert.Contains("Dose", nineteen);
        Assert.Contains("54 pt", nineteen);
        Assert.Contains("lands with C-2.1.1", nineteen);
    }
}
