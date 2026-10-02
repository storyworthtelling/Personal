using System.Reflection;
using System.Text.RegularExpressions;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Guard;

/// <summary>
/// The columns are each account's own choices, set up in Foreword — never names in the code.
///
/// <para>They were an enum of one household's four people. That put real names in the source of a
/// repository that is public, and it fixed the page to one family: a second household would have
/// had to edit the app. The only column the code may know by name is Watch, which is a category,
/// and the one a fresh install prints before anything is set up, which names nobody.</para>
///
/// <para>This guards the code, not the tests: the tests use an invented household on purpose.</para>
/// </summary>
public class ColumnsAreDataTests
{
    private static readonly string[] Allowed = ["Watch", "Me"];

    [Fact]
    public void TheOnlyColumnTheTypeNamesIsWatch()
    {
        var named = typeof(PersonColumn)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(PersonColumn))
            .Select(f => f.Name)
            .ToList();

        Assert.Equal(["Watch"], named);
    }

    [Fact]
    public void NoCodeBuildsAColumnFromAName()
    {
        var offenders = new List<string>();

        foreach (var project in new[] { "Foreword.Domain", "Foreword.Infrastructure", "Foreword.Android" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), project), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var source = File.ReadAllText(file);

                // "new PersonColumn("Someone")", and the target-typed "new("Someone")" the column type
                // itself uses for Watch.
                var patterns = new[]
                {
                    @"new\s+PersonColumn\s*\(\s*""(?<name>[^""]*)""\s*\)",
                    @"PersonColumn\s+\w+\s*=\s*new\s*\(\s*""(?<name>[^""]*)""\s*\)",
                };

                foreach (var pattern in patterns)
                {
                    foreach (Match match in Regex.Matches(source, pattern))
                    {
                        var name = match.Groups["name"].Value;
                        if (!Allowed.Contains(name))
                        {
                            offenders.Add($"{Path.GetFileName(file)}: a column called \"{name}\"");
                        }
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These write a column's name into the code. Columns are set up in Foreword and kept with "
            + "the account's settings; the code may only know Watch:\n" + string.Join("\n", offenders));
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Foreword.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
