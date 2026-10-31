namespace Constitution.Tests.Groceries;

/// <summary>D112: the head's share is wired the way the domain decided it, read from the source because the head cannot run here.</summary>
public class KeepShareWiringTests
{
    private static string Text(string suffix) =>
        File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => ConstitutionSource.Relative(file).EndsWith(suffix, StringComparison.Ordinal)));

    [Fact]
    public void TheSenderAimsAtOnePackageAndNeverAChooser()
    {
        var text = Text("Constitution.Android/MealsTab/KeepIntentSender.cs");

        Assert.Contains("Intent.ActionSend", text, StringComparison.Ordinal);
        Assert.Contains("SetPackage(decision.Package)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateChooser", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSenderMapsBothRefusalsToTypedAnswers()
    {
        var text = Text("Constitution.Android/MealsTab/KeepIntentSender.cs");

        Assert.Contains("catch (ActivityNotFoundException)", text, StringComparison.Ordinal);
        Assert.Contains("KeepStart.NotFound", text, StringComparison.Ordinal);
        Assert.Contains("KeepStart.Refused", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMealsBarOffersTheShareOnThisWeek()
    {
        var text = Text("Constitution.Android/MealsArea.cs");
        var bar = text[text.IndexOf("private View? ThisWeekBar()", StringComparison.Ordinal)..];

        Assert.Contains("KeepShareRun.ActionLabel", bar[..bar.IndexOf("private void OnSwitch", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public void TheShareTellsTheOwnerInlineAndNeverByToast()
    {
        var text = Text("Constitution.Android/MealsArea.cs");

        Assert.DoesNotContain("Toast", text, StringComparison.Ordinal);
        Assert.Contains("KeepShareRun.Finish(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheActivityGivesTheAreaItsShare()
    {
        Assert.Contains("mealsArea.Share = ", Text("Constitution.Android/ConstitutionActivity.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRunAndTheShareReadTheSameRecordFile()
    {
        var text = Text("Constitution.Android/MealWeek/MealWeekServices.cs");

        Assert.Contains("DecisionRecords(appContext));", text, StringComparison.Ordinal);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(text, @"new FileMealDecisionRecords\(", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));
    }
}
