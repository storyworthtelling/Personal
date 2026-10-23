using System.Text.RegularExpressions;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// The head's views are compile-checked, not run here, so what can be said about the Workouts tab's are said by reading
/// their source: no tap, no bar, no literal colour or size, the card over the whole screen, the skip through the fold, and
/// every decision taken from the model. Nothing here says the tab looks like the book; the owner has not compared them.
/// </summary>
public class WorkoutsTabSourceTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static IReadOnlyList<string> Files() => NoConfirmDialogTests.TabFiles();

    private static string All() => string.Join("\n", Files().Select(File.ReadAllText));

    private static IEnumerable<string> ViewFiles() =>
        Files().Where(file => Path.GetFileName(file) != "WorkoutCardView.cs");

    private static readonly Regex Literal = new(
        @"""#[0-9A-Fa-f]{6,8}""|Color\.ParseColor\(\s*""|SetTextSize\([^;]*,\s*[0-9]|TextSize\s*=\s*[0-9]",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Tap = new(
        @"SetOnClickListener|\bClick\s*\+=|\bClickable\s*=|SetClickable|HintChevron|[Cc]hevron",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Bar = new(@"BuildBottomBar|BottomBar\(|PrimaryButton|SecondaryButton|QuietButton|DestructiveButton", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public void NoColourOrTypeSizeIsWrittenOutInTheTab()
    {
        var offenders = Files().Where(file => Literal.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "A hex or a type size outside the token files:\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("x.SetTextColor(Color.ParseColor(\"#336699\"));")]
    [InlineData("var c = \"#FFFFFF\";")]
    [InlineData("view.SetTextSize(ComplexUnitType.Sp, 15f);")]
    public void TheLiteralDetectorFiresOnPlantedLiterals(string planted)
    {
        Assert.True(Literal.IsMatch(planted), planted);
    }

    [Fact]
    public void NoRowOpensAnythingOnATap()
    {
        var offenders = ViewFiles().Where(file => Tap.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "A tap listener or chevron on the tab's rows:\n" + string.Join("\n", offenders));
        Assert.Contains("LongClick", All(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTabHasNoPinnedBarAndNoButton()
    {
        var offenders = Files().Where(file => Bar.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "Workouts has no primary action, so no bar:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void LoadingAndNoPlanAreStatusSentences()
    {
        var area = Read("Constitution.Android/WorkoutsArea.cs");

        Assert.Contains("LoadingSentence", area, StringComparison.Ordinal);
        Assert.Contains("Loading = true", area, StringComparison.Ordinal);
        Assert.Contains("PageFurniture.Sentence", area, StringComparison.Ordinal);
        Assert.Contains("WorkoutsTabModel.NoPlan", area, StringComparison.Ordinal);
        Assert.Contains("IWorkoutsSource", area, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCardAndItsHazeCoverTheWholeScreenBinderIncluded()
    {
        var card = Read("Constitution.Android/WorkoutsTab/WorkoutCardView.cs");

        Assert.Contains("Android.Resource.Id.Content", card, StringComparison.Ordinal);
        Assert.Contains("PaperTheme.PaperDeep", card, StringComparison.Ordinal);
        Assert.Contains("DesignTokens.Shape.RadiusCard", card, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCardTurnsToTheDaysInPlace()
    {
        var card = Read("Constitution.Android/WorkoutsTab/WorkoutCardView.cs");

        Assert.Contains("ShowChoices", card, StringComparison.Ordinal);
        Assert.Contains("RemoveAllViews", card, StringComparison.Ordinal);
        Assert.Contains("IsEnabled", card, StringComparison.Ordinal);
    }

    [Fact]
    public void ASkipFoldsTheSharedDogEarAndNothingElse()
    {
        var area = Read("Constitution.Android/WorkoutsArea.cs");

        Assert.Contains("SkipUndo", area, StringComparison.Ordinal);
        Assert.Contains("ConnectionNoticeBoard.With", area, StringComparison.Ordinal);
        Assert.Contains("IUndoableArea", area, StringComparison.Ordinal);
        Assert.Contains("Due(", area, StringComparison.Ordinal);
        Assert.Contains("Reverse(", area, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShellHandsTheTurnedUndoToTheArea()
    {
        var shell = Read("Constitution.Android/ConstitutionActivity.cs");

        Assert.Contains("IUndoableArea", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDecisionIsTakenFromTheModels()
    {
        var text = All() + Read("Constitution.Android/WorkoutsArea.cs");

        foreach (var model in new[] { "WorkoutsTabModel.Build", "ActionCardModel.For", "SwapChoices.For", "MoveHere.For", "ClippingLayout.Place", "SwapChoices.Check" })
        {
            Assert.Contains(model, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ASkippedClippingIsStruckThroughAndAPressedOneInset()
    {
        var text = All();

        Assert.Contains("StrikeThruText", text, StringComparison.Ordinal);
        Assert.Contains("Pressed", text, StringComparison.Ordinal);
        Assert.Contains("ScrapInk.MagazineEdge", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TodayShowsTheWorkoutClippingFromItsModel()
    {
        var area = Read("Constitution.Android/TodayArea.cs");
        var page = Read("Constitution.Android/Today/TodayPage.cs");

        Assert.Contains("TodayModel.Clipping(plan)", area, StringComparison.Ordinal);
        Assert.Contains("ScrapKind.MagazineClipping", page, StringComparison.Ordinal);
    }
}
