using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace Foreword.Tests.Guard;

/// <summary>
/// No value is formatted with a <b>one-character</b> format string that cannot possibly be valid.
///
/// <para>This is not a style rule. <c>ToString("h")</c> does not mean "the hour": a format string of
/// exactly one character is read as a <i>standard</i> specifier, and "h" is not one of them, so the
/// call throws <see cref="FormatException"/> instead of returning anything. The fix is a leading
/// percent — <c>"%h"</c> — which forces the custom reading.</para>
///
/// <para>It earns a guard because of how it fails. It compiles, it reads correctly to anyone
/// scanning the line, and it throws only when that line is reached — so the work-shift row that
/// carried it took the Calendars screen down on the owner's phone, from a clean install, for every
/// shift the app ships with. <c>TimeFormats</c> already carried the fix and a comment explaining it;
/// the row had simply written its own copy, which is why the formatting now goes through one place.
/// </para>
/// </summary>
public class SingleCharacterFormatTests
{
    /// <summary>
    /// The types whose standard specifiers a single character might legitimately be. <c>Guid</c> is
    /// in the list because <c>Guid.NewGuid().ToString("n")</c> is correct and common, and a guard
    /// that flagged it would be turned off within a week.
    /// </summary>
    private static readonly Func<string, string>[] Formatters =
    [
        format => new DateTime(2026, 9, 12, 8, 0, 0, DateTimeKind.Utc).ToString(format, CultureInfo.InvariantCulture),
        format => new TimeOnly(8, 0).ToString(format, CultureInfo.InvariantCulture),
        format => new DateOnly(2026, 9, 12).ToString(format, CultureInfo.InvariantCulture),
        format => new TimeSpan(1, 2, 3).ToString(format, CultureInfo.InvariantCulture),
        format => Guid.Empty.ToString(format),
        format => 1234.5m.ToString(format, CultureInfo.InvariantCulture),
    ];

    /// <summary>
    /// Asked of the framework rather than remembered, so the set cannot drift from what actually
    /// throws — and so the surprises stay visible. <c>TimeOnly</c> in particular accepts far fewer
    /// standard specifiers than <c>DateTime</c>: <c>ToString("d")</c> is fine on a date and throws
    /// on a time.
    /// </summary>
    private static bool ThrowsForEveryType(char specifier) =>
        Formatters.All(format =>
        {
            try
            {
                format(specifier.ToString());
                return false;
            }
            catch (FormatException)
            {
                return true;
            }
        });

    /// <summary>
    /// The trap this exists for, stated as a test so the comment above is not the only record of it.
    /// </summary>
    [Fact]
    public void AOneCharacterHourFormatThrowsAndThePercentFormIsTheFix()
    {
        var time = new TimeOnly(8, 0);

        Assert.Throws<FormatException>(() => time.ToString("h"));
        Assert.Equal("8", time.ToString("%h", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NoCallSiteUsesASingleCharacterFormatThatAlwaysThrows()
    {
        string[] projects = ["Foreword.Domain", "Foreword.Infrastructure", "Foreword.Android"];

        var offenders = new List<string>();

        foreach (var project in projects)
        {
            foreach (var file in SourceFiles(Path.Combine(Root(), project)))
            {
                foreach (Match match in Regex.Matches(
                             File.ReadAllText(file), @"\.ToString\(\s*""(.)""\s*[,)]"))
                {
                    var specifier = match.Groups[1].Value[0];
                    if (ThrowsForEveryType(specifier))
                    {
                        offenders.Add(
                            $"{Path.GetRelativePath(Root(), file)}: ToString(\"{specifier}\") — "
                            + $"a one-character format is a STANDARD specifier. Use \"%{specifier}\", "
                            + "or a helper on TimeFormats.");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These throw at run time rather than formatting anything:\n" + string.Join("\n", offenders));
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

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
