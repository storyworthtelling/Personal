using System.Text.RegularExpressions;
using Xunit;

namespace Foreword.Tests.Design;

/// <summary>
/// The printed sheets draw from the registered ladder, not from numbers chosen at the call site.
///
/// <para>DESIGN.md registers <b>one</b> type scale, in sp, for the screens. The sheets are a
/// different medium and need points, and the answer to that has been eight different literals
/// scattered through the composers — 8, 9, 10, 11, 12, 16, 20, 22 — none of them derived from
/// anything. That is a second, unregistered ladder, which is the same shape of problem as a second
/// copy of the palette: nothing compares it to the system it is supposed to belong to, so it drifts
/// and nobody can see that it has.</para>
///
/// <para>D30 settles it: the printed ladder is the sp ladder multiplied by one registered ratio.
/// This guard is what stops a ninth literal appearing the next time a sheet needs a size.</para>
/// </summary>
public class PrintedTypeScaleTests
{
    /// <summary>
    /// No font size is a literal anywhere in the composers. The regex looks for a number passed
    /// where a size goes, which is exactly the shape the old code had.
    /// </summary>
    [Fact]
    public void NoComposerChoosesItsOwnPointSize()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(Root(), "Foreword.Infrastructure", "Pdf"), "*.cs"))
        {
            foreach (Match match in Regex.Matches(
                File.ReadAllText(file), @"\b(?:Sans|Serif)\(\s*(?<size>\d+(?:\.\d+)?f?)\b"))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value.Trim()}…");
            }
        }

        Assert.True(offenders.Count == 0,
            "These pick a point size at the call site instead of taking one from the registered "
            + "ladder, which is a second type scale nothing checks:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// No composer names a Skia colour. The sheets drew in <c>SKColors.Black</c>, <c>Gray</c> and
    /// <c>LightGray</c> — none of which is in the Paper &amp; Ink palette. The bible's ink is a warm
    /// near-black and its hairline a paper-cream grey; pure black on a rule of pure grey is a
    /// different, colder document that happens to look plausible, which is why nothing caught it.
    ///
    /// <para>The ground is the one exemption and it is deliberate: the sheets are drawn white for
    /// the newspaper look rather than on the device's warm ground, which is a design decision
    /// standing until the owner says otherwise.</para>
    ///
    /// <para><b>It also catches a hand-mixed colour</b>, which the first version of this guard did
    /// not. It looked only for <c>SKColors.</c> and passed happily over
    /// <c>new SKColor(230, 230, 245)</c> and <c>new SKColor(225, 238, 225)</c> — a lavender and a
    /// green sitting in the calendar composer, no more part of the palette than the named greys and
    /// harder to spot, because they read as deliberate.</para>
    /// </summary>
    [Fact]
    public void NoComposerNamesAColourInsteadOfARole()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(Root(), "Foreword.Infrastructure", "Pdf"), "*.cs"))
        {
            var source = File.ReadAllText(file);

            foreach (Match match in Regex.Matches(source, @"SKColors\.(?<name>\w+)"))
            {
                if (match.Groups["name"].Value == "White")
                {
                    continue;   // the ground, exempted above
                }

                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }

            foreach (Match match in Regex.Matches(source, @"new SKColor\([^)]*\d[^)]*\)"))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These name a Skia colour instead of a Paper & Ink role, so the sheets are drawn in a "
            + "palette the design system does not contain:\n" + string.Join("\n", offenders));
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
