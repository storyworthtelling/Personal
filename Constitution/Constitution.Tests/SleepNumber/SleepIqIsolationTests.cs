namespace Constitution.Tests.SleepNumber;

/// <summary>
/// C-7.2.1 — the unofficial API's whole shape lives in one class, and the session key lives in memory.
/// Reads the source, so a second file that learns an endpoint or writes the key down fails here.
/// </summary>
public class SleepIqIsolationTests
{
    private static IReadOnlyList<string> Sources() =>
        Directory.EnumerateFiles(Path.Combine(ConstitutionSource.Root(), "Constitution.Infrastructure", "SleepNumber"), "*.cs").ToList();

    [Fact]
    public void TheGuardFindsTheClassFolder()
    {
        Assert.Contains(Sources(), file => Path.GetFileName(file) == "SleepIqClient.cs");
        Assert.True(Sources().Count >= 8);
    }

    [Fact]
    public void OnlyTheClientNamesAnEndpointOrAField()
    {
        var shapeWords = new[] { "sleepiq.sleepnumber", "/rest/", "sleeperId", "totalSleepSessionTime", "sleepData", "\"_k\"", "_k=" };

        var offenders = Sources()
            .Where(file => Path.GetFileName(file) != "SleepIqClient.cs")
            .Where(file => shapeWords.Any(word => File.ReadAllText(file).Contains(word, StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, "SleepIQ's shape leaked out of SleepIqClient: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NothingWritesTheSessionKeyDown()
    {
        var storage = new[] { "File.", "Preferences", "SharedPreferences", "Directory.", "SecureStorage", "IsolatedStorage", "localStorage" };

        var offenders = Sources()
            .Where(file => storage.Any(word => File.ReadAllText(file).Contains(word, StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, "A SleepIQ file touches storage: " + string.Join(", ", offenders));
    }
}
