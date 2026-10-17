using System.Text.RegularExpressions;

namespace Constitution.Tests.Today;

/// <summary>
/// C-4.1.4 (book p.11, p.15): Today's rows open nothing, show no chevron and carry no press
/// handler; the one action is the bar's button. This reads the Today view source for listeners. The
/// single file that builds the bar's button is the only one allowed to hold one, and only one.
/// </summary>
public partial class TodayHasOneActionTests
{
    private const string BarFile = "TodayActionBar.cs";

    [GeneratedRegex(@"SetOnClickListener|SetOnLongClickListener|SetOnTouchListener|\bClick\s*\+=|\bLongClick\s*\+=|\.LongClick\b|\.Click\b|\bClickable\s*=|SetClickable|HintChevron|[Cc]hevron")]
    private static partial Regex Listener();

    internal static int Hits(string source) => Listener().Matches(source).Count;

    private static IReadOnlyList<string> TodayFiles() =>
        ConstitutionSource.Files(".cs")
            .Where(file =>
            {
                var relative = ConstitutionSource.Relative(file);
                return relative == "Constitution.Android/TodayArea.cs"
                    || relative.StartsWith("Constitution.Android/Today/", StringComparison.Ordinal);
            })
            .ToList();

    [Fact]
    public void TheGuardScansTheTodayFilesAndIsNotVacuous()
    {
        var files = TodayFiles();

        Assert.Contains(files, file => file.EndsWith("TodayArea.cs", StringComparison.Ordinal));
        Assert.Contains(files, file => file.EndsWith(BarFile, StringComparison.Ordinal));
        Assert.True(files.Count >= 3, "Today's page, its bar and its area should all be scanned: " + files.Count);
    }

    [Theory]
    [InlineData("row.SetOnClickListener(new Handler());")]
    [InlineData("row.SetOnLongClickListener(null);")]
    [InlineData("row.Click += OnTap;")]
    [InlineData("row.LongClick += OnHold;")]
    [InlineData("row.Clickable = true;")]
    [InlineData("row.SetClickable(true);")]
    [InlineData("PaperUi.HintChevron(host, Open);")]
    [InlineData("var chevron = Draw();")]
    public void ThePatternFiresOnPlantedListeners(string planted)
    {
        Assert.True(Hits("class Row\n{\n    void Build() { " + planted + " }\n}") > 0, planted);
    }

    [Fact]
    public void NoTodayFileOtherThanTheBarHoldsAListener()
    {
        var offenders = TodayFiles()
            .Where(file => !ConstitutionSource.IsGuard(file, BarFile))
            .Where(file => Hits(File.ReadAllText(file)) > 0)
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "Today's rows must do nothing on a tap: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheBarHoldsAtMostTheOneButtonsListener()
    {
        var bar = TodayFiles().Single(file => ConstitutionSource.IsGuard(file, BarFile));

        Assert.True(Hits(File.ReadAllText(bar)) <= 1);
    }

    [Fact]
    public void TheBarIsTheOnlyPlaceThatNamesTheAction()
    {
        var namers = TodayFiles()
            .Where(file => File.ReadAllText(file).Contains("TodayModel.ActionLabel", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file))
            .ToList();

        Assert.Equal([BarFile], namers);
    }
}
