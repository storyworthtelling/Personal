using System.Text.RegularExpressions;

namespace Pulp.Tests.Household;

/// <summary>
/// A screen that shows the household has to read it again when a sync pass changes it (the other phone's
/// records arriving, the status moving), and has to let go when it is not showing — the services live for
/// the process, so a screen that subscribed and never unsubscribed would be held (and redrawn) forever.
/// Read by content; each check asserts it found the screens.
/// </summary>
public class SyncWatchReachTests
{
    private static readonly string[] Screens =
    [
        "MainActivity.cs", "Title/TitleActivity.cs", "Settings/HouseholdActivity.cs", "Settings/ServiceActivity.cs", "FirstRun/FirstRunActivity.cs",
    ];

    private static IReadOnlyList<(string Path, string Text)> Head() =>
        PulpSource.Files(".cs")
            .Select(file => (Path: PulpSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Path.StartsWith("Pulp.Android/", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void EveryHouseholdScreenWatchesTheSyncWhileShowing()
    {
        var head = Head();
        foreach (var screen in Screens)
        {
            var file = head.SingleOrDefault(candidate => candidate.Path == "Pulp.Android/" + screen);
            Assert.False(file == default, screen + " was not found; the guard has nothing to read.");
            Assert.Contains("new SyncWatch(", file.Text, StringComparison.Ordinal);
            Assert.Matches(@"OnResume\(\)\s*\{[^}]*syncWatch\??\.Resume\(\)", file.Text);
            Assert.Matches(@"OnPause\(\)\s*\{[^}]*syncWatch\??\.Pause\(\)", file.Text);
        }
    }

    [Fact]
    public void OnlySyncWatchSubscribesToTheStatusAndItLetsGo()
    {
        var head = Head();
        var subscribers = head.Where(file => Regex.IsMatch(file.Text, @"SyncStatusChanged\s*\+=")).Select(file => file.Path).ToList();

        Assert.Equal(["Pulp.Android/App/SyncWatch.cs"], subscribers);
        var watch = head.Single(file => file.Path == "Pulp.Android/App/SyncWatch.cs").Text;
        Assert.Contains("SyncStatusChanged -= OnChanged", watch, StringComparison.Ordinal);
        Assert.Contains("RunOnUiThread", watch, StringComparison.Ordinal);
    }
}
