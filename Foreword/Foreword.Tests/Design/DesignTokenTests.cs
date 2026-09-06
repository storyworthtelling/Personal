using System.Text.RegularExpressions;
using Xunit;

namespace Foreword.Tests.Design;

/// <summary>
/// Foreword's side of the design system, which is now a short list because the system itself moved
/// out.
///
/// <para>This app used to hold a private copy of the whole Paper &amp; Ink dictionary, and this file
/// used to hold a second copy of the palette to check it against. Both copies could be wrong in the
/// same way — and were, for as long as nobody put two of the apps side by side: the accent was still
/// the retired terracotta and Display drew in the wrong face. The role table and the palette now
/// live once in <c>Storyworthtelling.Maui</c> and are checked there against
/// <c>Storyworthtelling.Paper</c>'s generated tokens by reflection, so no typed copy sits on the
/// expected side at all.</para>
///
/// <para>What is left here is the part that is genuinely this app's: that it introduces no colours
/// of its own, and that it has not quietly grown a second dictionary.</para>
/// </summary>
public class DesignTokenTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Foreword.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate Foreword.sln above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// No colour hex anywhere in this app — not "outside the token file", <b>anywhere</b>, because
    /// this app no longer has a token file. Every colour it draws comes from the shared kit by name.
    /// </summary>
    [Fact]
    public void NoColorHexLiteralsAnywhereInTheApp()
    {
        var root = Root();
        var allowedDirectories = new[]
        {
            // Generated adaptive-icon resources: IconStudio writes them from the registered icon
            // token file, and they are never hand-edited.
            Path.Combine("Foreword.Android", "Resources", "values"),
            Path.Combine("Foreword.Android", "Platforms", "Android", "Resources"),
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
                if (allowedDirectories.Any(dir => relative.StartsWith(dir, StringComparison.Ordinal)))
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
            "Color hex literals in an app that has no token file — every colour comes from the "
            + "shared kit by name:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// The app declares no <c>Color</c>, no <c>x:Double</c> size and no <c>Style</c> of its own.
    ///
    /// <para>The leak guard above catches a hex; this catches the subtler regression, which is a
    /// head growing a second dictionary that shadows the kit. A style redefined here would win
    /// silently over the shared one, which is exactly how the three heads drifted apart in the first
    /// place — nothing fails, the app just stops looking like the others.</para>
    /// </summary>
    [Fact]
    public void TheAppDeclaresNoStylesOfItsOwn()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Root(), "*.xaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Root(), file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            {
                continue;
            }

            var xaml = File.ReadAllText(file);
            foreach (var declaration in new[] { "<Style ", "<Color ", "<x:Double " })
            {
                if (xaml.Contains(declaration, StringComparison.Ordinal))
                {
                    offenders.Add($"{relative}: declares {declaration.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These shadow the shared kit, which fails silently — the app simply stops looking like "
            + "the rest of the suite:\n" + string.Join("\n", offenders));
    }
}
