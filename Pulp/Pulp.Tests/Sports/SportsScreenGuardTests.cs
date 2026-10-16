using System.Text.RegularExpressions;

namespace Pulp.Tests.Sports;

/// <summary>
/// The Sports screen's own rules, read by content because the head is not compiled in this suite
/// (D55, D242, G15). It draws from the sheets the services hand over and from nothing rawer; it carries
/// no word for how a game went; it keeps to the Paper tokens and to no Caution (D275); and a game's Play
/// never records itself as watched. Every check first asserts it found the screen, so none passes on
/// an empty folder.
/// </summary>
public class SportsScreenGuardTests
{
    private static IReadOnlyList<(string Path, string Text)> Screen() =>
        PulpSource.Files(".cs")
            .Select(file => (Path: PulpSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Path.StartsWith("Pulp.Android/Sports/", StringComparison.Ordinal)
                || file.Path == "Pulp.Android/Shell/SportsArea.cs"
                || (file.Path.StartsWith("Pulp.Domain/Sports/SportsScreen", StringComparison.Ordinal)))
            .ToList();

    private static IReadOnlyList<(string Path, string Text)> Head() => Screen().Where(file => file.Path.StartsWith("Pulp.Android/", StringComparison.Ordinal)).ToList();

    [Fact]
    public void TheGuardFindsTheScreenItChecks()
    {
        var files = Screen();

        Assert.Contains(files, file => file.Path == "Pulp.Android/Shell/SportsArea.cs");
        Assert.True(files.Count(file => file.Path.StartsWith("Pulp.Android/Sports/", StringComparison.Ordinal)) >= 3,
            "The Sports screen's files under Pulp.Android/Sports/ were not found.");
        Assert.True(files.Count(file => file.Path.StartsWith("Pulp.Domain/Sports/SportsScreen", StringComparison.Ordinal)) >= 5,
            "The Sports screen's pure files were not found.");
    }

    [Fact]
    public void NoScreenFileNamesARawFeedOrASource()
    {
        var raw = new Regex(@"\b(LeagueFeed|ISportsSource|SportsFeeds|LeagueSchedule|SportsCache|RoutedSportsSource|SportsSourceTable)\b|\bservices\.Sports\b|\)\s*\.Sports\b");
        var files = Screen();
        Assert.NotEmpty(files);

        var offenders = files.Where(file => raw.IsMatch(file.Text)).Select(file => file.Path).ToList();

        Assert.True(offenders.Count == 0, "These reach past the sheets to a raw feed or source:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoScreenFileHasAWordForHowAGameWent()
    {
        var scoreish = new Regex(@"\b(scores?|scored|scoring|scoreboard|points?|pts|wins?|won|loss|losses|lost|goals?|margin|results?)\b", RegexOptions.IgnoreCase);
        var files = Screen();
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(file => scoreish.Matches(file.Text).Select(match => $"{file.Path}: {match.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0, "A score-ish word on the Sports screen (D55):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoScreenFileDrawsCautionOrAColourLiteral()
    {
        var literal = new Regex(@"#[0-9A-Fa-f]{6}\b|Color\.(Rgb|Argb)\s*\(|\bCaution\b|CautionMark|CautionFill");
        var files = Screen();
        Assert.NotEmpty(files);

        var offenders = files.Where(file => literal.IsMatch(file.Text)).Select(file => file.Path).ToList();

        Assert.True(offenders.Count == 0, "Caution or a colour literal on the Sports screen:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryGameRowIsBuiltFromASheetAndTheHeadMakesNone()
    {
        var head = Head();
        Assert.NotEmpty(head);
        var made = new Regex(@"new\s+(GameRow|GameLine|Game)\s*\(");

        var offenders = head.Where(file => made.IsMatch(file.Text)).Select(file => file.Path).ToList();

        Assert.True(offenders.Count == 0, "A game row was made in the head instead of read from a sheet:\n" + string.Join("\n", offenders));
        Assert.Contains(head, file => file.Text.Contains("SportsScreenPlan.For(", StringComparison.Ordinal));
        Assert.Contains(head, file => file.Text.Contains("CachedSportsAsync(", StringComparison.Ordinal));
        Assert.Contains(head, file => file.Text.Contains(".SportsAsync(", StringComparison.Ordinal));
    }

    [Fact]
    public void APlayedGameIsNeverRecordedAsWatchedByTheHandOff()
    {
        // A hand-off's evidence reads as WATCHED for a title (Watched.Decide), so a game opened to look at would
        // vanish from the recordings and lift the hold. Play may call the one door (D311) and keep usage evidence,
        // but only through GamePlay.UsageSitting, which RecordedGames ignores (D312); the head never names the
        // evidence kind itself, and only "Mark watched" (or a session that ended) says a game was watched.
        var head = Head();
        Assert.NotEmpty(head);

        var offenders = head.Where(file => Regex.IsMatch(file.Text, @"WatchSource\.\w+")).Select(file => file.Path).ToList();
        Assert.True(offenders.Count == 0, "The Sports screen names a watch source itself:\n" + string.Join("\n", offenders));

        Assert.Contains(head, file => file.Text.Contains("MarkGameAsync(", StringComparison.Ordinal));
        var play = head.Single(file => file.Path == "Pulp.Android/Sports/SportsScreen.cs").Text;
        Assert.Contains("executor.Start(", play, StringComparison.Ordinal);
        Assert.Contains("GamePlay.UsageSitting(", play, StringComparison.Ordinal);

        // Every write to the watched records is one of the two allowed: the viewer's own mark, or the usage sitting.
        foreach (var file in head)
        {
            foreach (Match write in Regex.Matches(file.Text, @"HouseholdMaps\.Watched[^;]*;"))
            {
                Assert.Contains("sitting", write.Value, StringComparison.Ordinal);
            }
        }

        Assert.DoesNotMatch(new Regex(@"HandOffExecutor\s*\(|new\s+HandOffPlanner|HandOff\.For\s*\(|ActionView"), play);
    }

    [Fact]
    public void HighlightsAreDrawnFromTheGenericLabelAndNeverAVideosOwnWords()
    {
        // D314: a highlight's title or thumbnail is feed text and can say how a game went. The head never names the
        // league-wide Highlight record or its list, and never reads a title off the lookup (which returns an id only).
        var head = Head();
        Assert.NotEmpty(head);

        var offenders = head
            .Where(file => Regex.IsMatch(file.Text, @"\bHighlight\b|\.Highlights\b|\bsnippet\b|[Tt]humbnail"))
            .Select(file => file.Path)
            .ToList();

        Assert.True(offenders.Count == 0, "The Sports screen reads a video's own words:\n" + string.Join("\n", offenders));
        Assert.Contains(head, file => file.Text.Contains("HighlightsOffer.For(", StringComparison.Ordinal));
        Assert.Contains(head, file => file.Text.Contains("game.HighlightsLabel", StringComparison.Ordinal));
    }
}
