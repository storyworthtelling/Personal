using System.Reflection;
using System.Text.RegularExpressions;
using Storyworthtelling.Paper;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// The MAUI kit against the design system it is supposed to be.
///
/// <para>These read the kit's files by content, because the kit needs the MAUI/Android workload and
/// this suite runs without it. What matters more is the <b>expected</b> side: the palette is checked
/// against <see cref="PaperColorTokens"/> by reflection, not against a table typed into this file.
/// The three MAUI heads each had a private copy of the palette in their own dictionary and a leak
/// guard comparing it to a second private copy in their own test, so both copies could be — and
/// were — wrong in the same way for as long as nobody put two apps side by side.</para>
/// </summary>
public class PaperKitTests
{
    /// <summary>
    /// Every colour in the kit's dictionary is the value the generated token file carries under
    /// that name.
    ///
    /// <para>The live failure this was written against is <c>Accent</c>. DESIGN.md retires
    /// terracotta "everywhere" and the Action role is pine; the native heads moved at their rebrand
    /// phases and the MAUI dictionaries did not, so every button in Foreword, Ledger and Canonizer
    /// is still <c>#C2542F</c>. <see cref="PaperColorTokens"/> keeps the old <c>Accent</c> constant
    /// so existing renderers do not break, which is why this test maps the two accent keys onto the
    /// Action role explicitly rather than matching by name — the name is exactly what was
    /// misleading.</para>
    /// </summary>
    [Fact]
    public void EveryColourInTheKitIsTheGeneratedTokenOfThatName()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Accent"] = nameof(PaperColorTokens.ActionMark),
            ["AccentDeep"] = nameof(PaperColorTokens.ActionDeep),
        };

        var wrong = new List<string>();
        foreach (var (key, hex) in Colours())
        {
            // A wash is not its own value: §3 defines it as the role's mark at WashAlpha, so it is
            // composed here rather than compared to eight digits somebody typed. A hand-mixed wash
            // that looks about right is exactly what this catches.
            if (key.EndsWith("Wash", StringComparison.Ordinal))
            {
                var mark = Hex(key[..^4] + "Mark");
                var alpha = PaperColorTokens.WashAlpha.ToString("X2");
                if (mark is not null && hex != "#" + alpha + mark[1..])
                {
                    wrong.Add($"{key} is {hex}, but the mark at WashAlpha is #{alpha}{mark[1..]}");
                }

                continue;
            }

            var constant = expected.TryGetValue(key, out var mapped) ? mapped : key;
            var field = typeof(PaperColorTokens).GetField(constant, BindingFlags.Public | BindingFlags.Static);

            if (field is null)
            {
                wrong.Add($"{key}: no PaperColorTokens.{constant} to check it against");
                continue;
            }

            var value = ((string)field.GetValue(null)!).ToUpperInvariant();
            if (value != hex)
            {
                wrong.Add($"{key} is {hex}, but PaperColorTokens.{constant} is {value}");
            }
        }

        Assert.True(wrong.Count == 0,
            "The kit's palette has drifted from the generated tokens:\n" + string.Join("\n", wrong));
    }

    /// <summary>
    /// The type ladder matches the one the native heads draw from, read out of its own source file
    /// rather than retyped here. DESIGN.md §4's reworked scale reached
    /// <c>Codex.Domain.Design.DesignTokens.TypeSp</c> and never reached the MAUI dictionaries.
    /// </summary>
    [Fact]
    public void TheTypeLadderMatchesTheNativeOne()
    {
        var native = File.ReadAllText(RepoFile("FocusPhone/Codex.Domain/Design/DesignTokens.cs"));

        var wrong = new List<string>();
        foreach (var (role, size) in Sizes())
        {
            var match = Regex.Match(native, $@"public const (?:int|float) {role}\s*=\s*(?<value>\d+)");
            if (!match.Success)
            {
                wrong.Add($"{role}: no TypeSp.{role} in the native tokens to check it against");
                continue;
            }

            if (match.Groups["value"].Value != size)
            {
                wrong.Add($"{role} is {size} in the kit and {match.Groups["value"].Value} natively");
            }
        }

        Assert.True(wrong.Count == 0,
            "The kit's type ladder has drifted from the native one:\n" + string.Join("\n", wrong));
    }

    /// <summary>
    /// Every font family the kit names is one <see cref="PaperFonts"/> actually registers.
    ///
    /// <para>An unregistered alias is the quietest failure in MAUI: no error, no warning, no missing
    /// glyphs — the platform substitutes the system face and the screen simply looks a bit wrong, in
    /// a way that only shows up beside an app that got it right.</para>
    /// </summary>
    [Fact]
    public void EveryFontFamilyTheKitNamesIsRegistered()
    {
        var registered = Regex.Matches(
                File.ReadAllText(KitFile("PaperFonts.cs")),
                @"AddFont\(""[^""]+"",\s*""(?<alias>[^""]+)""\)")
            .Select(m => m.Groups["alias"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(registered);

        var used = Regex.Matches(Dictionary(), @"Property=""FontFamily"" Value=""(?<alias>[^""]+)""")
            .Select(m => m.Groups["alias"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var unregistered = used.Except(registered).OrderBy(a => a, StringComparer.Ordinal).ToList();

        Assert.True(unregistered.Count == 0,
            "These families are named in the kit but never registered, so MAUI silently substitutes "
            + "the system face:\n" + string.Join("\n", unregistered));
    }

    /// <summary>
    /// The nine roles, each with the face the table gives it. Display is <b>Serif Black</b> — the
    /// face DESIGN.md §4 reserves for every screen header — and the Label role takes the muted ink,
    /// not the full one. Both were wrong in all three MAUI heads, and the Label one is the sort of
    /// mistake that reads as a slightly heavy screen rather than as a bug.
    /// </summary>
    [Theory]
    [InlineData("DisplayLabel", "SerifBlack", "Ink")]
    [InlineData("TitleLabel", "SerifSemibold", "Ink")]
    [InlineData("HeadingLabel", "SerifSemibold", "Ink")]
    [InlineData("SecondaryLabel", "Sans", "InkMuted")]
    [InlineData("FieldLabel", "SansSemibold", "InkMuted")]
    [InlineData("MonoLabel", "Mono", "Ink")]
    [InlineData("EmptyStateLabel", "SerifItalic", "InkMuted")]
    [InlineData("VoiceLabel", "Script", "InkBlue")]
    public void TheRoleTableIsWhatPaperUiSays(string style, string family, string colour)
    {
        var declaration = Style(style);

        Assert.True(declaration is not null, $"The kit has no style named {style}.");
        Assert.Contains($@"Property=""FontFamily"" Value=""{family}""", declaration!, StringComparison.Ordinal);
        Assert.Contains($@"Property=""TextColor"" Value=""{{StaticResource {colour}}}""", declaration, StringComparison.Ordinal);
    }

    /// <summary>
    /// Serif Italic has exactly one role: the empty state. It is the most borrowable face in the
    /// set and the rule is what stops it becoming general-purpose emphasis.
    /// </summary>
    [Fact]
    public void SerifItalicHasExactlyOneRole()
    {
        var uses = Regex.Matches(Dictionary(), @"Property=""FontFamily"" Value=""SerifItalic""").Count;
        Assert.Equal(1, uses);
    }

    /// <summary>
    /// No letterspacing anywhere. The faces are drawn with their own fit and the bible spends no
    /// tracking; a screen that tracks its labels stops looking like the rest of the suite.
    /// </summary>
    [Fact]
    public void NothingIsLetterspaced()
    {
        Assert.DoesNotContain("CharacterSpacing", Dictionary(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Buttons sit at Label size and are sentence-case. Every button in the kit, not just the
    /// primary one — a secondary that shouts is the same mistake.
    /// </summary>
    [Fact]
    public void EveryButtonIsLabelSizedAndSentenceCase()
    {
        var wrong = new List<string>();
        foreach (Match button in Regex.Matches(
            Dictionary(), @"<Style(?: x:Key=""(?<key>[^""]+)"")? TargetType=""Button"".*?</Style>",
            RegexOptions.Singleline))
        {
            var key = button.Groups["key"].Success ? button.Groups["key"].Value : "(implicit)";
            if (!button.Value.Contains(@"Property=""FontSize"" Value=""{StaticResource LabelSize}""", StringComparison.Ordinal))
            {
                wrong.Add($"{key}: not at Label size");
            }

            if (button.Value.Contains("TextTransform", StringComparison.Ordinal))
            {
                wrong.Add($"{key}: transforms its text");
            }
        }

        Assert.NotEmpty(Regex.Matches(Dictionary(), @"TargetType=""Button"""));
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// Paper.xaml is the kit's only token file, so no colour hex appears anywhere else in it —
    /// views included. A view that mixes its own colour is how a component starts drifting from
    /// the table it is supposed to be an instance of.
    /// </summary>
    [Fact]
    public void NoColourHexOutsideTheDictionary()
    {
        var offenders = new List<string>();
        var kit = Path.Combine(RepoRoot(), "Storyworthtelling.Maui");

        foreach (var file in Directory.EnumerateFiles(kit, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(kit, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin")
                || Path.GetExtension(file) is not (".cs" or ".xaml")
                || relative.EndsWith("Paper.xaml", StringComparison.Ordinal))
            {
                continue;
            }

            if (Regex.IsMatch(File.ReadAllText(file), @"#[0-9A-Fa-f]{6,8}\b"))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(offenders.Count == 0,
            "Colour hex outside the kit's registered token file:\n" + string.Join("\n", offenders));
    }

    // ---- reading the kit ----

    private static string? Hex(string constant) =>
        (typeof(PaperColorTokens).GetField(constant, BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as string)?.ToUpperInvariant();

    private static IEnumerable<(string Key, string Hex)> Colours() =>
        Regex.Matches(Dictionary(), @"<Color x:Key=""(?<key>\w+)"">(?<hex>#[0-9A-Fa-f]{6,8})</Color>")
            .Select(m => (m.Groups["key"].Value, m.Groups["hex"].Value.ToUpperInvariant()));

    /// <summary>Kit key ("DisplaySize") to the native constant name ("Display") and its value.</summary>
    private static IEnumerable<(string Role, string Size)> Sizes() =>
        Regex.Matches(Dictionary(), @"<x:Double x:Key=""(?<key>\w+)Size"">(?<size>\d+)</x:Double>")
            .Select(m => (m.Groups["key"].Value, m.Groups["size"].Value));

    private static string? Style(string key)
    {
        var match = Regex.Match(
            Dictionary(), $@"<Style x:Key=""{key}""(?<body>.*?)</Style>", RegexOptions.Singleline);
        return match.Success ? match.Value : null;
    }

    private static string Dictionary() => File.ReadAllText(KitFile("Resources/Styles/Paper.xaml"));

    private static string KitFile(string relative) =>
        Path.Combine(RepoRoot(), "Storyworthtelling.Maui", relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoFile(string relative) =>
        Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
