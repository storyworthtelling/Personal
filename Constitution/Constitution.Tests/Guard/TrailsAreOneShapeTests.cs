using System.Text.RegularExpressions;

namespace Constitution.Tests.Guard;

/// <summary>Audit 08 §9–11: Constitution's trails, held to what the log loop needs from them.</summary>
public class TrailsAreOneShapeTests
{
    private static IEnumerable<(string Relative, string Text)> Android() =>
        ConstitutionSource.Files(".cs")
            .Select(file => (Relative: ConstitutionSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Relative.StartsWith("Constitution.Android/", StringComparison.Ordinal));

    [Fact]
    public void EveryWorkerFailureIsAlsoAWarningWithItsFrames()
    {
        // A type name at INFO was the whole report; the stack went nowhere.
        var missing = Android()
            .Where(file => file.Relative.EndsWith("Worker.cs", StringComparison.Ordinal))
            .Where(file => Regex.Matches(file.Text, @"worker failed: \{e\.GetType\(\)\.Name\}").Count
                != Regex.Matches(file.Text, @"\.Quiet\(""Worker"",").Count)
            .Select(file => file.Relative)
            .ToList();

        Assert.True(missing.Count == 0, "Add HeadDiagnostics.Current?.Quiet(\"Worker\", …, e) beside the trail line:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryTrailIsTheOneTrailClass()
    {
        // Four copies of the same file writer each wrote INFO whatever the phrase said.
        var copies = Android()
            .Where(file => file.Relative != "Constitution.Android/Services/ConstitutionTrail.cs")
            .Where(file => file.Text.Contains("new DiagnosticLog(", StringComparison.Ordinal))
            .Select(file => file.Relative)
            .ToList();

        Assert.True(copies.Count == 0, "Derive the trail from ConstitutionTrail:\n" + string.Join("\n", copies));
    }

    [Fact]
    public void TheMealsWriteTheirOwnTrail()
    {
        // The half-hourly meal pass rolled the 32 KB body.log within days.
        var intoBody = Android()
            .Where(file => file.Relative.StartsWith("Constitution.Android/MealWeek/", StringComparison.Ordinal))
            .Where(file => file.Text.Contains("new BodyLog(", StringComparison.Ordinal))
            .Select(file => file.Relative)
            .ToList();

        Assert.True(intoBody.Count == 0, "Write meal phrases to MealsLog:\n" + string.Join("\n", intoBody));
        Assert.Contains(Android(), file => file.Relative == "Constitution.Android/ConstitutionApplication.cs"
            && file.Text.Contains("MealsLog.FileName", StringComparison.Ordinal));
    }
}
