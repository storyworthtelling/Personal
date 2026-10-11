using System.Text.RegularExpressions;

namespace Pulp.Tests.Availability;

/// <summary>
/// The recorded plan has to reach the screens, which is a property of the head's call sites that no
/// Domain test can see: a screen that passes bare service ids silently reads every plan as ad-free.
/// Read by content, and each check asserts it found the calls it guards.
/// </summary>
public class PlanTierReachTests
{
    private static readonly Regex HomeCall = new(
        @"(TitleHomes\.Choose|ContinueRowText\.(Where|Home)|PlaylistPageRowText\.For)\s*\((?<args>[^;]*?)(HomeOverrides|\.HomeOverrides)", RegexOptions.Singleline);

    [Fact]
    public void EveryScreenReadsWhereATitlePlaysThroughTheHouseholdsPlans()
    {
        var calls = PulpSource.Files(".cs")
            .Select(file => (Path: PulpSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Path.StartsWith("Pulp.Android/", StringComparison.Ordinal))
            .SelectMany(file => HomeCall.Matches(file.Text).Select(match => (file.Path, Args: match.Groups["args"].Value)))
            .ToList();

        Assert.True(calls.Count >= 10, "Too few where-a-title-plays calls found in the head; the guard is not reading them: " + calls.Count);
        var bare = calls.Where(call => !call.Args.Contains("ServicePlans", StringComparison.Ordinal)).Select(call => call.Path + ": " + call.Args.Trim()).ToList();
        Assert.True(bare.Count == 0, "A screen builds ad-free plans from bare service ids:\n" + string.Join("\n", bare));
    }
}
