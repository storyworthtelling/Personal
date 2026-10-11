using System.Text.RegularExpressions;

namespace Pulp.Tests.Guard;

/// <summary>
/// The Household page clears and rebuilds its views on every change and every sync pass, so a field
/// made fresh each time loses what was half typed. Every text field on it goes through the one helper
/// that keeps the draft, and every household reload names why it ran so a sync pass can wait.
/// </summary>
public class HouseholdPageKeepsTypingTests
{
    private const string Path = "Pulp.Android/Settings/HouseholdActivity.cs";

    private static string Page() =>
        File.ReadAllText(PulpSource.Files(".cs").Single(file => PulpSource.Relative(file) == Path));

    [Fact]
    public void EveryTextFieldOnThePageKeepsItsDraft()
    {
        var page = Page();

        Assert.Contains("private EditText DraftField(", page, StringComparison.Ordinal);
        Assert.True(Regex.Matches(page, @"\bDraftField\(").Count >= 4, "The rename, add and search fields should all use DraftField.");
        Assert.DoesNotMatch(@"PaperUi\.(Mono)?Field\(", page.Replace("var field = PaperUi.Field(this, hint);", string.Empty));
    }

    [Fact]
    public void EveryReloadSaysWhyItRan()
    {
        var page = Page();

        Assert.DoesNotMatch(@"\bReload\(\)", page);
        Assert.Contains("ReloadPolicy.ShouldWait(", page, StringComparison.Ordinal);
        Assert.Contains("ReloadCause.Sync", page, StringComparison.Ordinal);
    }

    [Fact]
    public void APersonsRecordsAreWrittenInOneEditNeverOneByOne()
    {
        var page = Page();

        // A sync pass landing between a child's record and their rules makes the hand-off planner refuse
        // (D12): no ordering of separate writes avoids it, so the page applies the whole change at once.
        var start = page.IndexOf("private Task RunSteps(", StringComparison.Ordinal);
        Assert.True(start >= 0, "The page has no RunSteps to carry PersonEdits out.");
        var end = page.IndexOf(";\n", page.IndexOf("=>", start, StringComparison.Ordinal), StringComparison.Ordinal);
        var body = page[start..end];
        Assert.Contains("EditHouseholdAsync(", body, StringComparison.Ordinal);
        Assert.Contains("PersonEdits.ApplyAll(", body, StringComparison.Ordinal);

        var outside = page[..start] + page[end..];
        Assert.DoesNotMatch(@"HouseholdMaps\.Viewer\b", outside);
        Assert.DoesNotMatch(@"RemoveAsync\(HouseholdMaps\.ChildRules", outside);
        // Editing one existing child's rules (a band, a block) is a single record and needs no atomic edit.
    }
}
