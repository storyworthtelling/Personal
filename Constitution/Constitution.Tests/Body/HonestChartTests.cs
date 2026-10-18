using System.Reflection;
using System.Text.RegularExpressions;
using Constitution.Domain.Body;
using Constitution.Tests.Guard;

namespace Constitution.Tests.Body;

/// <summary>
/// Body's charts are one neutral line against the owner's own past (D44, D69). This guard holds the
/// four things that would quietly turn a chart into a judgement: a member named for a goal, a norm,
/// a score, a streak or a target; a colour (or a good/bad) anywhere on the geometry; a way for the
/// signal's verdict to change the line's shape; and a painter that reaches for any ink but the
/// neutral one. Each half is proved non-vacuous by a planted sample it must catch.
/// </summary>
public class HonestChartTests
{
    /// <summary>Word stems no Body or chart member may begin with; matched per PascalCase word, so "Start" is not "Star".</summary>
    private static readonly string[] JudgementStems =
    [
        "goal", "norm", "scor", "streak", "target", "benchmark", "percentile", "population", "peer", "ideal", "healthy",
    ];

    /// <summary>Words that would make a geometry member a colour or a verdict.</summary>
    private static readonly string[] ColourWords =
    [
        "color", "colour", "hex", "ink", "role", "brush", "paint", "tone", "fill", "stroke", "style", "alpha", "rgb",
        "good", "bad", "better", "worse", "verdict", "worsening",
    ];

    private static readonly Regex Words = new(
        "[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    internal static IReadOnlyList<string> WordsOf(string identifier) =>
        Words.Matches(identifier).Select(match => match.Value.ToLowerInvariant()).ToList();

    internal static bool IsJudgement(string identifier) =>
        WordsOf(identifier).Any(word => JudgementStems.Any(stem => word.StartsWith(stem, StringComparison.Ordinal)));

    private static bool IsColour(string identifier) => WordsOf(identifier).Any(ColourWords.Contains);

    private static IReadOnlyList<Type> BodyTypes() =>
        typeof(BodyReading).Assembly.GetTypes()
            .Where(type => type.Namespace == "Constitution.Domain.Body" && !type.Name.StartsWith('<'))
            .ToList();

    /// <summary>Every name a type exposes: itself, members, parameters, nested types and enum values.</summary>
    private static IEnumerable<string> NamesOf(Type type)
    {
        yield return type.Name;
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;
        foreach (var member in type.GetMembers(all))
        {
            if (member.Name.StartsWith('<') || member.Name.StartsWith('.') || member.Name == "EqualityContract")
            {
                continue;
            }

            yield return member.Name;
            if (member is MethodBase method)
            {
                foreach (var parameter in method.GetParameters().Where(p => p.Name is not null))
                {
                    yield return parameter.Name!;
                }
            }
        }
    }

    // ---- no member is named for a judgement ----

    [Fact]
    public void NoBodyMemberIsNamedForAJudgement()
    {
        var types = BodyTypes();
        var names = types.SelectMany(NamesOf).Distinct().ToList();
        Assert.True(types.Count >= 9, $"The guard found only {types.Count} Body types; it is not reading the real model.");
        Assert.True(names.Count >= 60, $"The guard found only {names.Count} Body names; it is not reading the real model.");

        var offenders = names.Where(IsJudgement).ToList();
        Assert.True(offenders.Count == 0, "Body names that read as a judgement: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheJudgementDetectorCatchesPlantedNames()
    {
        foreach (var planted in new[]
        {
            "TargetWeight", "StreakDays", "PopulationNorm", "HealthScore", "GoalSteps", "NormalRange", "Scoring",
            "benchmarkLine", "PercentileRank", "IdealSleep", "peerAverage",
        })
        {
            Assert.True(IsJudgement(planted), planted + " should have been caught.");
        }

        foreach (var innocent in new[] { "Latest", "Movement", "Start", "Stronger", "Previous", "VerticalMargin", "Lines" })
        {
            Assert.False(IsJudgement(innocent), innocent + " should have passed.");
        }
    }

    // ---- the geometry carries no colour and no verdict ----

    [Fact]
    public void TheGeometryCarriesNoColourOrVerdict()
    {
        var shapeTypes = new[] { typeof(ChartGeometry), typeof(ChartPoint), typeof(ChartBridge) };
        var names = shapeTypes.SelectMany(NamesOf).Distinct().ToList();
        Assert.True(names.Count >= 15, "The guard found too few names on the geometry; it is not reading it.");

        var offenders = names.Where(IsColour).ToList();
        Assert.True(offenders.Count == 0, "Geometry names that read as a colour or a verdict: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheGeometryHoldsOnlyPlacesAndReadings()
    {
        var allowed = new HashSet<Type>
        {
            typeof(double), typeof(double?), typeof(DateOnly), typeof(DateOnly?), typeof(ChartPoint), typeof(ChartPoint?),
            typeof(IReadOnlyList<ChartPoint>), typeof(IReadOnlyList<IReadOnlyList<ChartPoint>>), typeof(IReadOnlyList<ChartBridge>),
            typeof(ChartBridge),
        };
        var shapeTypes = new[] { typeof(ChartGeometry), typeof(ChartPoint), typeof(ChartBridge) };
        var properties = shapeTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(property => property.Name != "EqualityContract")
            .ToList();

        Assert.True(properties.Count >= 9);
        var strays = properties.Where(property => !allowed.Contains(property.PropertyType))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}: {property.PropertyType.Name}").ToList();
        Assert.True(strays.Count == 0, "Geometry properties that are not a place or a reading: " + string.Join(", ", strays));
    }

    [Fact]
    public void ComputeCannotBeToldWhichWayIsWorse()
    {
        var compute = typeof(ChartGeometry).GetMethod(nameof(ChartGeometry.Compute))!;
        var parameterTypes = compute.GetParameters().Select(parameter => parameter.ParameterType).ToList();

        Assert.Equal(
            [typeof(IEnumerable<BodyReading>), typeof(BodyWindow), typeof(DateOnly), typeof(double), typeof(double)],
            parameterTypes);
        Assert.DoesNotContain(typeof(RisingIs), parameterTypes);
        Assert.DoesNotContain(typeof(SignalChange), parameterTypes);
    }

    // ---- the painter uses only neutral ink (source half) ----

    private static readonly Regex HexLiteral = new(
        @"#[0-9A-Fa-f]{6,8}\b|\b0x[0-9A-Fa-f]{6,8}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex RawColour = new(
        @"\bColor\.[A-Za-z]+|\bResource\.Color\b|\bColor\.Parse", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex ThemeToken = new(
        @"\bPaperTheme\.(?<name>[A-Za-z]+)\b(?!\s*\()", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex TokenFile = new(
        @"\bDesignTokens\.(?<name>[A-Za-z]+)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Identifier = new(
        @"[A-Za-z][A-Za-z0-9]*", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Comments = new(
        @"//[^\r\n]*|/\*.*?\*/", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(2));

    /// <summary>Paper and ink only: no role colour (action, problem, caution, progress, reference, resolved, identity).</summary>
    private static readonly string[] NeutralTheme =
        ["Ink", "InkSoft", "InkMuted", "Hairline", "Paper", "PaperBright", "PaperDeep", "PaperShade"];

    private static readonly string[] NeutralTokens =
        ["Ink", "InkSoft", "InkMuted", "Hairline", "Paper", "PaperBright", "PaperDeep", "PaperShade", "Space", "Shape", "TypeSp", "Type", "Motion"];

    /// <summary>The head's Body files: a Body folder, or a Body, Signal or chart-line file. The scrap paper's painter is not one.</summary>
    internal static bool IsBodyFile(string relativePath)
    {
        var parts = relativePath.Split('/');
        if (parts.Length < 2 || parts[0] != "Constitution.Android" || !relativePath.EndsWith(".cs", StringComparison.Ordinal))
        {
            return false;
        }

        var name = parts[^1];
        return parts[1..^1].Contains("Body")
            || name.StartsWith("Body", StringComparison.Ordinal)
            || name.StartsWith("Signal", StringComparison.Ordinal)
            || name.StartsWith("ChartLine", StringComparison.Ordinal)
            || name.StartsWith("HonestChart", StringComparison.Ordinal);
    }

    internal static IEnumerable<Finding> PainterFindings(string relativePath, string text)
    {
        if (!IsBodyFile(relativePath))
        {
            yield break;
        }

        var code = Comments.Replace(text, match => new string(match.Value.Select(c => c == '\n' ? '\n' : ' ').ToArray()));
        foreach (Match match in HexLiteral.Matches(code))
        {
            yield return new Finding(GuardScan.LineOf(code, match.Index), "a colour literal");
        }

        foreach (Match match in RawColour.Matches(code))
        {
            yield return new Finding(GuardScan.LineOf(code, match.Index), "a raw colour");
        }

        foreach (Match match in ThemeToken.Matches(code))
        {
            if (!NeutralTheme.Contains(match.Groups["name"].Value))
            {
                yield return new Finding(GuardScan.LineOf(code, match.Index), "a role colour (" + match.Groups["name"].Value + ")");
            }
        }

        foreach (Match match in TokenFile.Matches(code))
        {
            if (!NeutralTokens.Contains(match.Groups["name"].Value))
            {
                yield return new Finding(GuardScan.LineOf(code, match.Index), "a design token that is not neutral (" + match.Groups["name"].Value + ")");
            }
        }

        foreach (Match match in Identifier.Matches(code))
        {
            if (IsJudgement(match.Value))
            {
                yield return new Finding(GuardScan.LineOf(code, match.Index), "a name that reads as a judgement");
            }
        }
    }

    [Fact]
    public void BodyPaintersUseOnlyNeutralInk()
    {
        var result = GuardScan.Run(ConstitutionSource.Root(), PainterFindings);

        Assert.True(result.Hits.Count == 0, "Body head files that break the honest-chart rules:\n" + string.Join("\n", result.Hits));
    }

    [Fact]
    public void TheSelectorFindsBodyFiles()
    {
        Assert.True(IsBodyFile("Constitution.Android/BodyArea.cs"));
        Assert.True(IsBodyFile("Constitution.Android/Body/ChartLinePainter.cs"));
        Assert.True(IsBodyFile("Constitution.Android/Scrapbook/SignalChartView.cs"));
        Assert.True(IsBodyFile("Constitution.Android/Body/Anything.cs"));
        Assert.False(IsBodyFile("Constitution.Android/Scrapbook/ChartScrapPainter.cs"));
        Assert.False(IsBodyFile("Constitution.Android/TodayArea.cs"));
        Assert.False(IsBodyFile("Constitution.Domain/Body/ChartGeometry.cs"));
        Assert.False(IsBodyFile("Constitution.Android/Body/Notes.md"));
    }

    [Fact]
    public void ThePainterDetectorCatchesPlants()
    {
        // Assembled at run time so this file holds no colour literal of its own.
        var hash = "#";
        var hex = hash + "33" + "33" + "33";
        var samples = new Dictionary<string, string>
        {
            ["a colour literal"] = $"var ink = Color.ParseColor(\"{hex}\");",
            ["a raw colour"] = "paint.Color = Color.Red;",
            ["a role colour"] = "paint.Color = PaperTheme.ProblemMark;",
            ["a design token that is not neutral"] = "var c = DesignTokens.PositiveMark;",
            ["a name that reads as a judgement"] = "var targetLine = 3;",
        };

        foreach (var (kind, line) in samples)
        {
            var found = PainterFindings("Constitution.Android/Body/ChartLinePainter.cs", line + "\n").ToList();
            Assert.Contains(found, finding => finding.Kind.StartsWith(kind, StringComparison.Ordinal));
        }

        var neutral = "paint.Color = PaperTheme.Ink;\nvar dp = PaperTheme.Dp(context, 4);\nvar t = DesignTokens.Space.X1;\n";
        Assert.Empty(PainterFindings("Constitution.Android/Body/ChartLinePainter.cs", neutral));
        Assert.Empty(PainterFindings("Constitution.Android/Body/ChartLinePainter.cs", "// the target stays out: no goal here\n"));
        Assert.Empty(PainterFindings("Constitution.Android/TodayArea.cs", "var c = Color.Red;"));
    }

    [Fact]
    public void ThePainterGuardFailsOnAPlantedFile()
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, "Constitution.Android/Body/ChartLinePainter.cs", "paint.Color = PaperTheme.CautionMark;\n");
            GuardScan.Plant(root, "Constitution.Android/TodayArea.cs", "paint.Color = PaperTheme.CautionMark;\n");

            var result = GuardScan.Run(root.FullName, PainterFindings);

            var hit = Assert.Single(result.Hits);
            Assert.Equal("Constitution.Android/Body/ChartLinePainter.cs", hit.File);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
