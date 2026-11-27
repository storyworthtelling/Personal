using System.Text.RegularExpressions;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.LockedOpen;
using Xunit;

namespace Pulp.Tests.LockedOpen;

/// <summary>
/// The timed release Pulp asks Flyleaf for before a streaming title opens (the Entertainment block): how
/// long the window is, that the title's length reaches the hand-off, and that Pulp's copy of the contract
/// is Flyleaf's own. Guard G20 on Pulp's side.
/// </summary>
public class TimedReleaseTests
{
    [Fact]
    public void AFilmsWindowIsItsRuntimePlusTheGrace()
    {
        Assert.Equal(104 + TimedReleaseHandOff.GraceMinutes, TimedReleaseHandOff.WindowMinutes(104));
    }

    [Fact]
    public void AUnknownLengthTakesTheUnknownWindow()
    {
        // A sporting event's feed carries no runtime, so it gets the unknown-length window plus the grace.
        Assert.Equal(TimedReleaseHandOff.UnknownLengthMinutes + TimedReleaseHandOff.GraceMinutes, TimedReleaseHandOff.WindowMinutes(null));
        Assert.Equal(TimedReleaseHandOff.UnknownLengthMinutes + TimedReleaseHandOff.GraceMinutes, TimedReleaseHandOff.WindowMinutes(0));
    }

    [Fact]
    public void AWindowNeverPassesFlyleafsCap()
    {
        Assert.Equal(TimedReleaseHandOff.MaxMinutes, TimedReleaseHandOff.WindowMinutes(1000));
    }

    [Fact]
    public void TheDiscoverFactsCarryTheTitlesRuntimeToTheHandOff()
    {
        var title = new CatalogueTitle
        {
            Id = new TitleId("title-larkspur"),
            Kind = TitleKind.Film,
            Name = "Larkspur",
            Genres = ["Thriller"],
            RuntimeMinutes = 112,
            Offers = [],
        };

        var facts = DiscoverFacts.For(title, _ => []);

        Assert.Equal(112, facts.RuntimeMinutes);
        Assert.Equal(112 + TimedReleaseHandOff.GraceMinutes, TimedReleaseHandOff.WindowMinutes(facts.RuntimeMinutes));
    }

    [Fact]
    public void Flyleafs_CopyOfTheContractIsTheSameContract()
    {
        // Guard G20: Flyleaf's TimedReleaseHandoff is the other end. A drift on either side fails here.
        var flyleaf = ReadRepoFile("FocusPhone", "Codex.Domain", "Apps", "TimedRelease.cs");
        var expected = new Dictionary<string, string>
        {
            ["Package"] = TimedReleaseHandOff.Package,
            ["Receiver"] = TimedReleaseHandOff.Receiver,
            ["Action"] = TimedReleaseHandOff.Action,
            ["PackageExtra"] = TimedReleaseHandOff.PackageExtra,
            ["LinkExtra"] = TimedReleaseHandOff.LinkExtra,
            ["MinutesExtra"] = TimedReleaseHandOff.MinutesExtra,
        };
        foreach (var (name, value) in expected)
        {
            Assert.Matches(new Regex($"public const string {name} = \"{Regex.Escape(value)}\";"), flyleaf);
        }

        Assert.Matches($"public const int ResultReleased = {TimedReleaseHandOff.ResultReleased};", flyleaf);
        Assert.Matches($"public const int ResultRefused = {TimedReleaseHandOff.ResultRefused};", flyleaf);
        Assert.Matches($"public const int MaxMinutes = {TimedReleaseHandOff.MaxMinutes};", flyleaf);
        Assert.Matches($"public const int MinMinutes = {TimedReleaseHandOff.MinMinutes};", flyleaf);
        Assert.Contains("public const string Permission = LockedOpenHandoff.Permission;", flyleaf, StringComparison.Ordinal);
        Assert.Contains($"\"{TimedReleaseHandOff.Permission}\"", ReadRepoFile("FocusPhone", "Codex.Domain", "Apps", "LockedOpen.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheHandOffExecutorStartsARelease()
    {
        // The one door (G8): a release is asked for only from the executor, which runs only after the planner allowed the title.
        var offenders = Directory.EnumerateFiles(ReadRepoRoot("Pulp", "Pulp.Android"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("TimedReleaseCourier.cs", StringComparison.Ordinal)
                && !file.EndsWith("HandOffExecutor.cs", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("TimedReleaseCourier", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheExecutorAsksForTheReleaseBeforeItRoutesTheTitle()
    {
        var executor = File.ReadAllText(Path.Combine(ReadRepoRoot("Pulp", "Pulp.Android"), "HandOffs", "HandOffExecutor.cs"));
        var release = executor.IndexOf("TimedReleaseCourier.Send(", StringComparison.Ordinal);
        var route = executor.IndexOf("return Route(host, plan, serviceName, handing);", StringComparison.Ordinal);

        Assert.True(release > 0, "The executor never asks for a timed release.");
        Assert.True(route > release, "The title is routed before the release is asked for.");
    }

    private static string ReadRepoRoot(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Pulp")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, Path.Combine(segments));
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var path = ReadRepoRoot(segments);
        Assert.True(File.Exists(path), $"Couldn’t find {path}.");
        return File.ReadAllText(path);
    }
}
