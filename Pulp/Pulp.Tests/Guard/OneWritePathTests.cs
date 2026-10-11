using System.Text.RegularExpressions;

namespace Pulp.Tests.Guard;

/// <summary>
/// Three screens edited a playlist or a child's pick, and each kept its own way to do it: a whole-list
/// snapshot written back on undo, a pick removal built from the picker's open-time copy, fire-and-forget
/// writes an undo could overtake, and two new-playlist id formats. Each lost something without a word.
/// These guards keep the one write path the only one. Each asserts it found what it protects, so none
/// passes on an empty tree.
/// </summary>
public class OneWritePathTests
{
    private static IReadOnlyList<(string Path, string Text)> Head() =>
        PulpSource.Files(".cs")
            .Select(file => (Path: PulpSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Path.StartsWith("Pulp.Android/", StringComparison.Ordinal))
            .ToList();

    private static string Text(string path) =>
        Head().Single(file => file.Path == path).Text;

    private static void OnlyIn(Regex pattern, Func<string, bool> allowed, string what)
    {
        var hits = Head().Where(file => pattern.IsMatch(file.Text)).Select(file => file.Path).ToList();

        Assert.True(hits.Any(allowed), $"The one place that {what} was not found; the guard has nothing to protect.");
        var strays = hits.Where(path => !allowed(path)).ToList();
        Assert.True(strays.Count == 0, $"Only one place {what}; also found in: {string.Join(", ", strays)}");
    }

    /// <summary>
    /// A screen writing a <c>HouseholdPlaylist</c> record writes the list it read, not the list there is.
    /// A brand-new playlist goes through <c>CreatePlaylistAsync</c> too, so there is no exception.
    /// </summary>
    [Fact]
    public void OnlyTheServicesWriteAPlaylistRecord() =>
        OnlyIn(
            new Regex(@"HouseholdMaps\.Playlist\b"),
            path => path == "Pulp.Android/App/PulpServices.cs",
            "writes a playlist record (against the household as it stands)");

    [Fact]
    public void OnlyTheServicesWriteAChildsPick() =>
        OnlyIn(
            new Regex(@"HouseholdMaps\.ParentPick\b"),
            path => path == "Pulp.Android/App/PulpServices.cs",
            "writes a child's pick (removed by key, D255)");

    [Fact]
    public void OnlyThePickerWritesHandleAPickerChange() =>
        OnlyIn(
            new Regex(@"case\s+PickerChange\.RemoveUndoFirst|==\s*PickerChange\.RemoveUndoFirst"),
            path => path is "Pulp.Android/Components/PickerWrites.cs" or "Pulp.Android/Components/AddToPicker.cs",
            "turns a picker tick into a household write");

    [Fact]
    public void ANewPlaylistTakesTheOneIdFormat()
    {
        var ownFormat = new Regex(@"new\s+PlaylistId\s*\([^;]*Guid\.NewGuid");
        var strays = Head().Where(file => ownFormat.IsMatch(file.Text)).Select(file => file.Path).ToList();

        Assert.True(strays.Count == 0, "A new-playlist id made in its own format: " + string.Join(", ", strays));
        Assert.Contains(Head(), file => file.Text.Contains("HouseholdEdits.NewPlaylistId()", StringComparison.Ordinal));
    }

    /// <summary>The lookup keeps the found id (the key's daily quota) and refuses a non-id before it reaches the player.</summary>
    [Fact]
    public void OnlyTheTrailerLookupAsksForATrailer() =>
        OnlyIn(
            new Regex(@"\.FindTrailerAsync\s*\("),
            path => path == "Pulp.Android/Title/TrailerLookup.cs",
            "asks the trailer source");

    [Fact]
    public void TheKeptWeekHasOneStore()
    {
        var made = Head().Sum(file => Regex.Matches(file.Text, @"new\s+DiscoverWeekStore\s*\(").Count);

        Assert.Equal(1, made);
    }

    [Theory]
    [InlineData("Pulp.Android/Watching/WatchingActions.cs")]
    [InlineData("Pulp.Android/Shell/DiscoverArea.cs")]
    public void AScreensWritesGoThroughOneQueue(string path)
    {
        var text = Text(path);

        Assert.Contains("new LibraryWrites(", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"Func<Task>\s+write\b"), text);
    }

    /// <summary>A pile button that fails says so above the stack; it never takes the stack away.</summary>
    [Theory]
    [InlineData("Trailer")]
    [InlineData("AddTo")]
    public void AStackButtonThatFailsKeepsTheStack(string method)
    {
        var text = Text("Pulp.Android/Shell/DiscoverArea.cs");
        var body = Regex.Match(text, @"private void " + method + @"\(DiscoverRow row\)(?<body>.*?)\n    private ", RegexOptions.Singleline);

        Assert.True(body.Success, $"DiscoverArea has no {method}(DiscoverRow) to check.");
        Assert.Contains("RunOnStack(", body.Groups["body"].Value, StringComparison.Ordinal);
    }
}
