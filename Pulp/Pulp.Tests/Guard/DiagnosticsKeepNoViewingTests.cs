using System.Text.RegularExpressions;

namespace Pulp.Tests.Guard;

/// <summary>
/// Settings tells the owner the diagnostics hold "never what anyone watched", and the export goes to a
/// share chooser. A log line that interpolates a title id, a viewer id or a title's name breaks that
/// promise: a failed hand-off would record which child was playing which title. Lines say the event
/// kind and the outcome only. This reads every Diagnostics call in the head and asserts it found them.
/// </summary>
public partial class DiagnosticsKeepNoViewingTests
{
    // An interpolation hole naming an id, a name, a viewer, a title or the like.
    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex Hole();

    [GeneratedRegex(@"\bdiagnostics\.Warn\(", RegexOptions.IgnoreCase)]
    private static partial Regex Call();

    [GeneratedRegex(@"\b(id|viewer|child|title|row|seed|franchise|playlist|service|who|person|what)\b|\w*(Id|Name|Title)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Viewing();

    // The file's own name in the tally log is a constant, not anything watched.
    private static readonly string[] HarmlessHoles = ["FileName"];

    /// <summary>The text of a call from its name to its matching close paren, skipping string literals.</summary>
    private static string Statement(string text, int start)
    {
        var open = text.IndexOf('(', start);
        var depth = 0;
        var inString = false;
        for (var at = open; at < text.Length; at++)
        {
            var c = text[at];
            if (inString)
            {
                if (c == '\\')
                {
                    at++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && --depth == 0)
            {
                return text[start..(at + 1)];
            }
        }

        throw new InvalidOperationException("A Diagnostics call never closes.");
    }

    private static IEnumerable<(string Path, string Statement)> Calls() =>
        PulpSource.Files(".cs")
            .Select(file => (Path: PulpSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Path.StartsWith("Pulp.Android/", StringComparison.Ordinal))
            .SelectMany(file => Call().Matches(file.Text)
                .Select(match => (file.Path, Statement: Statement(file.Text, match.Index))));

    private static bool Offends(string statement) =>
        Hole().Matches(statement).Any(hole =>
            !HarmlessHoles.Contains(hole.Groups[1].Value.Trim()) && Viewing().IsMatch(hole.Groups[1].Value));

    [Fact]
    public void TheHeadLogsThroughDiagnosticsAndTheGuardFindsIt()
    {
        Assert.True(Calls().Count() > 20, "Expected the head's many Diagnostics.Warn calls; the guard has nothing to check.");
    }

    [Fact]
    public void NoDiagnosticsLineInterpolatesAnIdAViewerOrATitle()
    {
        var offences = Calls()
            .Where(call => Offends(call.Statement))
            .Select(call => $"{call.Path}: {call.Statement.Split('\n')[0].Trim()}")
            .ToList();

        Assert.True(offences.Count == 0, "Diagnostics must not record what anyone watched:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void ThePatternCatchesAPlantedLineSoItIsNotVacuous()
    {
        var planted = "services.Diagnostics.Warn(\"child\", $\"Play on '{title.Id}' failed.\", exception)";
        Assert.Contains(Hole().Matches(planted), hole => Viewing().IsMatch(hole.Groups[1].Value));
        var clean = "services.Diagnostics.Warn(\"child\", $\"Play failed ({exception.Failure}).\", exception)";
        Assert.DoesNotContain(Hole().Matches(clean), hole => Viewing().IsMatch(hole.Groups[1].Value));
    }
}
