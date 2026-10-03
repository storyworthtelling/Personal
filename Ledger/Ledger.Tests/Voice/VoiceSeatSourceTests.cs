using System.Text.RegularExpressions;
using Index.Tests.Design;

namespace Index.Tests.Voice;

/// <summary>
/// R-G29 (e) and (g), and the wiring of the three seats (remediation 3.7, owner decision R5). The Android
/// head cannot be loaded by this desktop-TFM project, so what has to be true of the pages is read out of
/// their files — the pattern <c>SavingsPageSourceTests</c> and <c>FirstRunSourceTests</c> use.
///
/// <para>(e): no seat in first run or in an empty state — the line is "what the balances are doing", and
/// with no balances there is nothing for it to be about. (g): nothing of the prompt, the profile or the
/// sentence is logged, and the only place that builds a model request is the composer, which takes its
/// words from the pinned instructions.</para>
/// </summary>
public class VoiceSeatSourceTests
{
    private const string MainPath = "Ledger.Android/Pages/MainPage.xaml.cs";
    private const string SnowballPath = "Ledger.Android/Pages/SnowballPage.xaml.cs";
    private const string SavingsPath = "Ledger.Android/Pages/SavingsPage.xaml.cs";
    private const string ComposerPath = "Ledger.Infrastructure/Voice/VoiceSeatComposer.cs";

    // ---- one place builds a request, from the pinned words ----

    [Fact]
    public void OnlyTheComposerBuildsAModelRequestAndItNamesTheSeatsOccasion()
    {
        var builders = SourceFiles()
            .Where(file => File.ReadAllText(file).Contains("new MessageRequest(", StringComparison.Ordinal))
            .Select(file => file.Replace('\\', '/'))
            .ToList();

        Assert.Single(builders);
        Assert.EndsWith(ComposerPath, builders[0], StringComparison.Ordinal);
        Assert.Matches(@"new MessageRequest\(\s*prompt\.Occasion\s*,\s*prompt\.Context\s*,\s*prompt\.MaxWords\s*,\s*HighVisibility:\s*true",
            Source(ComposerPath));
    }

    [Fact]
    public void AccountsNoLongerAsksForAWelcomeInOneFixedSentence()
    {
        var main = Source(MainPath);

        Assert.DoesNotContain("the owner is checking their balances", main, StringComparison.Ordinal);
        Assert.DoesNotContain("\"welcome\"", main, StringComparison.Ordinal);
        Assert.DoesNotContain("VoicedMessenger", main, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowWelcomeAsync", main, StringComparison.Ordinal);
    }

    // ---- (e) first run and empty states ----

    [Fact]
    public void AccountsComposesNothingInFirstRunAndChecksAgainAfterTheComposeAwaits()
    {
        var voice = MethodBody(Source(MainPath), "private async Task ShowVoiceAsync(");

        var firstCompose = voice.IndexOf("ComposeAsync(", StringComparison.Ordinal);
        Assert.True(firstCompose > 0, "ShowVoiceAsync must compose through the seat composer.");
        Assert.Contains("firstRunShowing", voice[..firstCompose], StringComparison.Ordinal);
        Assert.Contains("firstRunShowing", voice[firstCompose..], StringComparison.Ordinal);
    }

    [Fact]
    public void FirstRunStillHidesTheLineExplicitly()
    {
        var firstRun = MethodBody(Source(MainPath), "private async Task RenderFirstRunAsync(");

        Assert.Contains("voiceLabel.IsVisible = false", firstRun, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private async Task RenderAccountsAsync(")]
    [InlineData("private Task RenderAccountsFromHouseholdAsync(")]
    public void SavingsComposesOnlyOnceTheSidesAreShownNeverOnTheEmptyPage(string signature)
    {
        var render = MethodBody(Source(SavingsPath), signature);

        var empty = render.IndexOf("ShowEmptyPage(", StringComparison.Ordinal);
        var sides = render.IndexOf("ShowSides(", StringComparison.Ordinal);
        var voice = render.IndexOf("ShowVoiceAsync(", StringComparison.Ordinal);
        Assert.True(empty >= 0 && sides > empty && voice > sides,
            "ShowVoiceAsync must come after ShowSides, i.e. after the empty branch has returned.");
    }

    [Fact]
    public void SavingsHidesTheLineWithTheRestOfTheAccountsSideOnTheEmptyPage()
    {
        var page = Source(SavingsPath);

        Assert.Contains("voiceLabel.IsVisible = false", MethodBody(page, "private void ShowEmptyPage("), StringComparison.Ordinal);
        Assert.DoesNotContain("ShowVoiceAsync(", MethodBody(page, "private void ShowEmptyPage("), StringComparison.Ordinal);
        Assert.DoesNotContain("ShowVoiceAsync(", MethodBody(page, "private void ShowNewerFile("), StringComparison.Ordinal);
        // The compose awaits Drive; if the page went empty in the meantime the answer is dropped.
        Assert.Contains("emptyHost.IsVisible", MethodBody(page, "private async Task ShowVoiceAsync("), StringComparison.Ordinal);
    }

    [Fact]
    public void SnowballComposesOnlyWhenThereAreLoansAndTheLoansSideIsTheOneShowing()
    {
        var voice = MethodBody(Source(SnowballPath), "private async Task ShowVoiceAsync(");

        Assert.Contains("VoiceFactBuilder.Snowball(", voice, StringComparison.Ordinal);
        Assert.Contains("showingNewerFile", voice, StringComparison.Ordinal);
    }

    // ---- placement: where the mockups draw it ----

    [Fact]
    public void SnowballsLineIsTheFirstThingOnTheLoansSideAboveDebtFreeAndNotOnHistory()
    {
        var page = Source(SnowballPath);
        var loansRoot = MethodBody(page, "private void BuildLoansSideRoot(");
        var historyRoot = MethodBody(page, "private void BuildHistorySideRoot(");

        var line = loansRoot.IndexOf("Children.Add(voiceLabel)", StringComparison.Ordinal);
        var debtFree = loansRoot.IndexOf("\"Debt-free\"", StringComparison.Ordinal);
        Assert.True(line >= 0 && debtFree > line, "The line sits above DEBT-FREE.");
        Assert.DoesNotContain("voiceLabel", historyRoot, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingsLineIsTheFirstThingOnTheAccountsSideAboveSavedAndNotOnGrowth()
    {
        var page = Source(SavingsPath);
        var accountsRoot = MethodBody(page, "private void BuildAccountsSideRoot(");

        Assert.DoesNotContain("voiceLabel", MethodBody(page, "private void BuildGrowthSideRoot("), StringComparison.Ordinal);
        var line = accountsRoot.IndexOf("Children.Add(voiceLabel)", StringComparison.Ordinal);
        var saved = accountsRoot.IndexOf("Children.Add(totalColumn)", StringComparison.Ordinal);
        Assert.True(line >= 0 && saved > line, "The line sits under the switch and above SAVED.");
    }

    [Theory]
    [InlineData(MainPath)]
    [InlineData(SnowballPath)]
    [InlineData(SavingsPath)]
    public void EveryLineWearsTheKitsVoiceStyleAndStartsHidden(string path)
    {
        var source = Source(path);

        Assert.Contains("VoiceSeatLine.Create(", source, StringComparison.Ordinal);
        var helper = Source("Ledger.Android/Views/VoiceSeatLine.cs");
        Assert.Contains("PaperResources.Token<Style>(\"VoiceLabel\")", helper, StringComparison.Ordinal);
        Assert.Contains("IsVisible = false", helper, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MainPath)]
    [InlineData(SnowballPath)]
    [InlineData(SavingsPath)]
    public void ThePageNeverWaitsOnTheModel(string path)
    {
        var source = Source(path);

        Assert.DoesNotMatch(@"await\s+ShowVoiceAsync\(", source);
        Assert.Matches(@"_\s*=\s*ShowVoiceAsync\(", source);
    }

    // ---- her install: the same facts, from the household snapshot ----

    [Fact]
    public void HerInstallBuildsTheSameFactsFromTheSnapshotsSeries()
    {
        var main = Source(MainPath);
        Assert.Contains("VoiceFactBuilder.Accounts(", main, StringComparison.Ordinal);
        Assert.Contains("household.Snapshot?.SnowballHistory", main, StringComparison.Ordinal);

        var snowball = Source(SnowballPath);
        Assert.Contains("VoiceFactBuilder.Snowball(", snowball, StringComparison.Ordinal);
        Assert.Contains("lastHousehold?.SnowballHistory", snowball, StringComparison.Ordinal);

        Assert.Contains("VoiceFactBuilder.Savings(summary.Accounts)",
            MethodBody(Source(SavingsPath), "private Task RenderAccountsFromHouseholdAsync("), StringComparison.Ordinal);
    }

    [Fact]
    public void TheOwnersFactsComeFromTheSameSeriesConversionThePublisherUses()
    {
        Assert.Contains("HouseholdChartSeries.FromSnowballHistory(", Source(MainPath), StringComparison.Ordinal);
        Assert.Contains("HouseholdChartSeries.FromSnowballHistory(", Source(SnowballPath), StringComparison.Ordinal);
    }

    [Fact]
    public void TheComposerIsRegisteredOnceForAllThreePages()
    {
        var program = Source("Ledger.Android/MauiProgram.cs");

        Assert.Contains("AddSingleton<VoiceSeatComposer>", program, StringComparison.Ordinal);
        var at = program.IndexOf("AddSingleton<VoiceSeatComposer>", StringComparison.Ordinal);
        Assert.Contains("GoogleAccountEmail", program.Substring(at, Math.Min(900, program.Length - at)), StringComparison.Ordinal);
    }

    // ---- (g) nothing of the prompt, the profile or the sentence reaches a log ----

    [Fact]
    public void TheComposerLogsOnlyTheSeatsNameNeverWhatItSaidOrWasToldOrKnows()
    {
        var composer = Source(ComposerPath);
        var logCalls = Regex.Matches(composer, @"logger\??\.Log\w+\((?<args>[^;]*)\);", RegexOptions.Singleline)
            .Select(match => match.Value)
            .ToList();

        Assert.NotEmpty(logCalls);
        foreach (var call in logCalls)
        {
            Assert.DoesNotMatch(
                new Regex(@"\b(prompt|Context|profile|reply|line|sentence|facts\.Lines|Lines|request|message)\b",
                    RegexOptions.IgnoreCase), call);
            Assert.DoesNotContain("ex.Message", call, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("Console.", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("Debug.Write", composer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MainPath)]
    [InlineData(SnowballPath)]
    [InlineData(SavingsPath)]
    public void NoPageLogsAVoicedSentenceOrItsFacts(string path)
    {
        var source = Source(path);
        foreach (Match call in Regex.Matches(source, @"logger\??\.Log\w+\((?<args>[^;]*)\);", RegexOptions.Singleline))
        {
            Assert.DoesNotMatch(@"(?i)\b(voice|facts|sentence|prompt)", call.Value);
        }
    }

    // ---- helpers ----

    private static IEnumerable<string> SourceFiles()
    {
        var root = DesignTestHelpers.LedgerRoot();
        foreach (var project in new[] { "Ledger.Domain", "Ledger.Infrastructure", "Ledger.Android" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (!normalized.Contains("/obj/") && !normalized.Contains("/bin/") && !normalized.Contains("/Migrations/"))
                {
                    yield return file;
                }
            }
        }
    }

    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find `{signature}`.");

        var open = source.IndexOf('{', source.IndexOf(')', start) >= 0 ? source.IndexOf(')', start) : start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after `{signature}`.");
    }
}
