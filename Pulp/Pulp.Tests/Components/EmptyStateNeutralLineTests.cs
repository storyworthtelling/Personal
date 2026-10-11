using System.Text.RegularExpressions;
using Pulp.Domain.Voice;

namespace Pulp.Tests.Components;

/// <summary>
/// Every empty page is voiced, each with a neutral Serif italic fallback (D264, mockup 13). The
/// fallbacks are the head's copy, in <c>Pulp.Android/Components/EmptyState.cs</c>, so this guard reads
/// that file by content (the suite never builds the Android head): every seat on
/// <see cref="VoicedOccasions"/> whose fallback is <see cref="VoiceFallback.NeutralLine"/> must have an
/// arm <c>VoiceSeat.Name =&gt; "a non-empty line"</c>.
///
/// <para>The bug it reintroduces: a new empty-page seat added to the closed list with no neutral line,
/// so on a phone with no profile or key the page is simply blank — or, worse, the switch's default arm
/// throws on first paint.</para>
/// </summary>
public class EmptyStateNeutralLineTests
{
    private const string EmptyStateFile = "Pulp.Android/Components/EmptyState.cs";

    [Fact]
    public void EveryNeutralLineSeatHasALineWrittenInTheHead()
    {
        var seats = VoicedOccasions.All
            .Where(occasion => occasion.Fallback == VoiceFallback.NeutralLine)
            .Select(occasion => occasion.Seat)
            .ToList();
        Assert.True(seats.Count >= 8,
            $"Expected the eight empty-page seats of D264 and D112; VoicedOccasions gave {seats.Count}.");

        var path = Path.Combine(PulpSource.Root(), EmptyStateFile);
        Assert.True(File.Exists(path), $"{EmptyStateFile} is missing, so no empty page has a neutral line.");
        var source = File.ReadAllText(path);

        var lines = NeutralLines(source);
        var missing = seats.Where(seat => !lines.ContainsKey(seat.ToString())).ToList();
        Assert.True(missing.Count == 0,
            "These empty-page seats have no neutral line in EmptyState.cs (D264): " + string.Join(", ", missing));
        Assert.All(seats, seat => Assert.False(string.IsNullOrWhiteSpace(lines[seat.ToString()])));
    }

    [Fact]
    public void NoNeutralLineIsWrittenForASeatThatHidesInstead()
    {
        var source = File.ReadAllText(Path.Combine(PulpSource.Root(), EmptyStateFile));
        var lines = NeutralLines(source);
        Assert.NotEmpty(lines);

        var hiding = VoicedOccasions.All
            .Where(occasion => occasion.Fallback == VoiceFallback.Hide)
            .Select(occasion => occasion.Seat.ToString())
            .ToList();
        Assert.NotEmpty(hiding);
        Assert.DoesNotContain(lines.Keys, hiding.Contains);
    }

    [Fact]
    public void TheDrawnLinesAreTheDrawnLines()
    {
        var lines = NeutralLines(File.ReadAllText(Path.Combine(PulpSource.Root(), EmptyStateFile)));

        // Mockup 13 draws these two word for word.
        Assert.Equal("Nothing started yet. Discover has this week's stack.", lines[nameof(VoiceSeat.EmptyContinueWatching)]);
        Assert.Equal("That's the week. More on Friday.", lines[nameof(VoiceSeat.StackUsedUp)]);
    }

    [Fact]
    public void TheReaderReadsOnlyTheNeutralLineSwitch()
    {
        const string source = """
            public static string NeutralLine(VoiceSeat seat) => seat switch
            {
                VoiceSeat.EmptySports => "No teams followed yet.",
                _ => throw new ArgumentOutOfRangeException(nameof(seat)),
            };

            public static string? PrimaryLabel(VoiceSeat seat) => seat switch
            {
                VoiceSeat.EmptyCalendar => "Not a neutral line",
                _ => null,
            };
            """;

        var lines = NeutralLines(source);

        Assert.Equal("No teams followed yet.", Assert.Single(lines).Value);
        Assert.Equal("EmptySports", lines.Keys.Single());
    }

    [Fact]
    public void AFileWithNoNeutralLineSwitchIsAFailureNotAPass() =>
        Assert.ThrowsAny<Exception>(() => NeutralLines("VoiceSeat.EmptySports => \"x\","));

    // The arms of the NeutralLine switch only: VoiceSeat.Name => "text".
    private static Dictionary<string, string> NeutralLines(string source)
    {
        var method = Regex.Match(source, @"string\s+NeutralLine\s*\(\s*VoiceSeat\s+\w+\s*\)\s*=>\s*\w+\s+switch\s*\{(?<body>.*?)\};", RegexOptions.Singleline);
        Assert.True(method.Success, "EmptyState.cs has no `string NeutralLine(VoiceSeat seat) => seat switch { ... };` to read.");

        var arm = new Regex(@"VoiceSeat\.(\w+)\s*=>\s*""((?:[^""\\]|\\.)*)""");
        return arm.Matches(method.Groups["body"].Value)
            .GroupBy(match => match.Groups[1].Value)
            .ToDictionary(group => group.Key, group => Regex.Unescape(group.Single().Groups[2].Value));
    }
}
