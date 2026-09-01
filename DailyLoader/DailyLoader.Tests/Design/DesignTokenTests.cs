using System.Text.RegularExpressions;
using Xunit;

namespace DailyLoader.Tests.Design;

/// <summary>
/// DailyLoader has no C# design consumers, so its only token file is the XAML
/// dictionary. These tests keep it honest against the Paper &amp; Ink palette in
/// DESIGN.md (the expected values here are the sanctioned test-side copy) and
/// enforce the no-literals rule everywhere else.
/// </summary>
public class DesignTokenTests
{
    private static readonly IReadOnlyDictionary<string, string> Bible = new Dictionary<string, string>
    {
        ["Paper"] = "#FAF8F2",
        ["PaperBright"] = "#FFFFFF",
        ["PaperDeep"] = "#F1EBDD",
        ["PaperShade"] = "#E9E1CF",
        ["Hairline"] = "#E3DCC9",
        ["Ink"] = "#33322B",
        ["InkSoft"] = "#6B6759",
        ["InkMuted"] = "#8A8578",
        ["Accent"] = "#C2542F",
        ["AccentDeep"] = "#9C4023",
    };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DailyLoader.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate DailyLoader.sln above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void DesignTokensXamlMatchesTheBiblePalette()
    {
        var path = Path.Combine(Root(), "DailyLoader.Android", "Resources", "Styles", "DesignTokens.xaml");
        Assert.True(File.Exists(path), $"Missing {path}");

        var parsed = Regex.Matches(File.ReadAllText(path), "<Color x:Key=\"(\\w+)\">(#[0-9A-Fa-f]{6})</Color>")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant());

        Assert.Equal(Bible.Count, parsed.Count);
        foreach (var (name, hex) in Bible)
        {
            Assert.True(parsed.TryGetValue(name, out var actual), $"DesignTokens.xaml missing color '{name}'");
            Assert.Equal(hex, actual);
        }
    }

    [Fact]
    public void ButtonStylesUseTheSoftStationeryRadius()
    {
        var path = Path.Combine(Root(), "DailyLoader.Android", "Resources", "Styles", "DesignTokens.xaml");
        var xaml = File.ReadAllText(path);
        var radii = Regex.Matches(xaml, "<Setter Property=\"CornerRadius\" Value=\"(\\d+)\" />")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();
        Assert.NotEmpty(radii);
        Assert.All(radii, r => Assert.Equal(12, r));
    }

    [Fact]
    public void NoColorHexLiteralsOutsideTokenFiles()
    {
        var root = Root();
        var allowedSuffixes = new[]
        {
            Path.Combine("DailyLoader.Android", "Resources", "Styles", "DesignTokens.xaml"),
            Path.Combine("DailyLoader.Tests", "Design", "DesignTokenTests.cs"),
        };
        var allowedDirectories = new[]
        {
            Path.Combine("DailyLoader.Android", "Platforms", "Android", "Resources"),
        };
        var offenders = new List<string>();

        foreach (var pattern in new[] { "*.cs", "*.xaml" })
        {
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
                {
                    continue;
                }
                if (allowedSuffixes.Any(suffix => file.EndsWith(suffix, StringComparison.Ordinal)) ||
                    allowedDirectories.Any(dir => relative.StartsWith(dir, StringComparison.Ordinal)))
                {
                    continue;
                }
                var source = File.ReadAllText(file);
                if (Regex.IsMatch(source, pattern == "*.cs" ? "\"#[0-9A-Fa-f]{6,8}\"" : "#[0-9A-Fa-f]{6,8}\\b"))
                {
                    offenders.Add(relative);
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Color hex literals outside token files:\n" + string.Join("\n", offenders));
    }
}
