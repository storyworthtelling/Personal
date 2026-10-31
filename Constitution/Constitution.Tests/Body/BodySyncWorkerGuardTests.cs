using System.Text.RegularExpressions;
using Constitution.Domain;

namespace Constitution.Tests.Body;

/// <summary>
/// C-7.1.3 — the Body sync worker's registered Java name, held to the frozen constant by reading the worker's own source.
/// The other guards check the literals in <c>ConstitutionIdentity</c>; none checks that the class registers itself under
/// them, which is the half that actually decides whether WorkManager can build a row it persisted.
/// </summary>
public class BodySyncWorkerGuardTests
{
    private static readonly Regex Registration = new(
        @"\[(?:global::)?(?:Android\.Runtime\.)?Register\(\s*(?:""(?<literal>[^""]+)""|ConstitutionIdentity\.(?<constant>\w+))\s*\)\]",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>The Java name a source text registers its class under, resolved to its value; null when it registers none.</summary>
    internal static string? RegisteredName(string source)
    {
        var match = Registration.Match(source);
        if (!match.Success)
        {
            return null;
        }

        if (match.Groups["literal"].Success)
        {
            return match.Groups["literal"].Value;
        }

        return match.Groups["constant"].Value switch
        {
            nameof(ConstitutionIdentity.BodySyncWorkerName) => ConstitutionIdentity.BodySyncWorkerName,
            nameof(ConstitutionIdentity.ReadBackWorkerName) => ConstitutionIdentity.ReadBackWorkerName,
            nameof(ConstitutionIdentity.WorkoutWeekWorkerName) => ConstitutionIdentity.WorkoutWeekWorkerName,
            nameof(ConstitutionIdentity.MealWeekWorkerName) => ConstitutionIdentity.MealWeekWorkerName,
            _ => "unknown constant " + match.Groups["constant"].Value,
        };
    }

    private static string WorkerSource()
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(path => Path.GetFileName(path) == "BodySyncWorker.cs");
        Assert.True(file is not null, "BodySyncWorker.cs was not found under Constitution/.");
        return File.ReadAllText(file!);
    }

    [Fact]
    public void TheWorkerRegistersUnderTheFrozenName()
    {
        Assert.Equal(ConstitutionIdentity.ApplicationId + ".BodySyncWorker", RegisteredName(WorkerSource()));
        Assert.Equal(ConstitutionIdentity.BodySyncWorkerName, RegisteredName(WorkerSource()));
    }

    [Fact]
    public void TheWorkerSchedulesUnderTheFrozenWorkNameAndKeeps()
    {
        var source = WorkerSource();

        Assert.Contains("EnqueueUniquePeriodicWork(ConstitutionIdentity.BodySyncWorkName", source, StringComparison.Ordinal);
        Assert.Contains("ExistingPeriodicWorkPolicy.Keep", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Replace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Update", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkIsScheduledAtAppStart()
    {
        var activity = File.ReadAllText(ConstitutionSource.Files(".cs").Single(path => Path.GetFileName(path) == "ConstitutionActivity.cs"));

        Assert.Contains("BodySyncWorker.Schedule(this)", activity, StringComparison.Ordinal);
        Assert.Contains("BodyServices.RefreshAtOpen(this)", activity, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[Register(\"example.app.BodySyncWorker\")]", "example.app.BodySyncWorker")]
    [InlineData("[global::Android.Runtime.Register(\"com.example.Moved\")]", "com.example.Moved")]
    [InlineData("[Register(ConstitutionIdentity.BodySyncWorkerName)]", ConstitutionIdentity.BodySyncWorkerName)]
    [InlineData("[Register(ConstitutionIdentity.ReadBackWorkerName)]", ConstitutionIdentity.ReadBackWorkerName)]
    [InlineData("public class NotRegistered : Worker {}", null)]
    public void TheDetectorReadsLiteralsAndConstants(string source, string? expected)
    {
        Assert.Equal(expected, RegisteredName(source));
    }

    [Fact]
    public void AMovedLiteralWouldFailTheGuard()
    {
        var moved = WorkerSourceWith("[Register(ConstitutionIdentity.BodySyncWorkerName)]", "[Register(\"example.app.sync.BodySyncWorker\")]");

        Assert.NotEqual(ConstitutionIdentity.BodySyncWorkerName, RegisteredName(moved));
    }

    private static string WorkerSourceWith(string was, string now)
    {
        var source = WorkerSource();
        Assert.Contains(was, source, StringComparison.Ordinal);
        return source.Replace(was, now, StringComparison.Ordinal);
    }
}
