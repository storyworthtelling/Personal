using System.Text.RegularExpressions;

namespace Pulp.Tests.HandOff;

/// <summary>
/// G13, structurally. <c>HandOff.For</c> is public and knows nothing of children, so a screen that
/// called it — or built its own VIEW intent from a link — would open a service for a child with the
/// gate never asked, and nothing would look wrong. So there is one door: the Android head starts a
/// hand-off only through <c>HandOffExecutor</c>, which only asks <c>HandOffPlanner</c>, which routes
/// any child or group through <c>ChildHandOff</c>. Read by content, because the head is not compiled
/// in this suite.
/// </summary>
public class HandOffExecutorIsTheOneDoorTests
{
    private const string ExecutorPath = "Pulp.Android/HandOffs/HandOffExecutor.cs";
    private const string PlannerPath = "Pulp.Domain/HandOffs/HandOffPlanner.cs";
    private const string ChildHandOffPath = "Pulp.Domain/Children/ChildHandOff.cs";

    private static readonly Regex HandOffForCall = new(@"(?<![\w.])(?:Pulp\.Domain\.Services\.)?HandOff\s*\.\s*For\s*\(");
    private static readonly Regex ChildHandOffForCall = new(@"\bChildHandOff\s*\.\s*For\s*\(");

    // Anything that builds or names a VIEW intent, or touches the raw intent record.
    private static readonly Regex ViewIntent = new(
        @"\bActionView\b|android\.intent\.action\.VIEW|\bViewAction\b|\bHandOffIntent\b|\bHandOffPlanner\b|\bSetPackage\s*\(");

    private static IReadOnlyList<string> SourceUnder(string folder) =>
        PulpSource.Files(".cs")
            .Where(file => PulpSource.Relative(file).StartsWith(folder + "/", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void OnlyTheExecutorBuildsAViewIntentInTheHead()
    {
        var files = SourceUnder("Pulp.Android");
        Assert.Contains(files, file => PulpSource.Relative(file) == ExecutorPath);

        var offenders = files
            .Where(file => PulpSource.Relative(file) != ExecutorPath)
            .Select(file => (file, text: File.ReadAllText(file)))
            .SelectMany(hit => new[] { ViewIntent.Match(hit.text), HandOffForCall.Match(hit.text), ChildHandOffForCall.Match(hit.text) }
                .Where(match => match.Success)
                .Select(match => $"{PulpSource.Relative(hit.file)}: {match.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "A hand-off was started outside HandOffExecutor, past the child gate:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheExecutorSendsOnlyThePlannersIntent()
    {
        var text = File.ReadAllText(Path.Combine(PulpSource.Root(), ExecutorPath));

        Assert.Matches(@"\bHandOffPlanner\b", text);
        Assert.Matches(@"\.Plan\s*\(", text);
        Assert.Matches(@"\bIntent\.ActionView\b", text);
        Assert.Matches(@"\bSetPackage\s*\(", text);
        // Never the gate-blind builders, and never the request's raw link: only plan.Intent reaches Android.
        Assert.DoesNotMatch(HandOffForCall, text);
        Assert.DoesNotMatch(ChildHandOffForCall, text);
        Assert.DoesNotMatch(@"\.Link\b", text);
    }

    [Fact]
    public void InTheDomainOnlyThePlannerAndTheChildGateBuildAnIntent()
    {
        var files = SourceUnder("Pulp.Domain").Concat(SourceUnder("Pulp.Infrastructure")).ToList();
        Assert.Contains(files, file => PulpSource.Relative(file) == PlannerPath);
        Assert.Contains(files, file => PulpSource.Relative(file) == ChildHandOffPath);

        var planner = File.ReadAllText(Path.Combine(PulpSource.Root(), PlannerPath));
        Assert.Matches(ChildHandOffForCall, planner);

        var offenders = files
            .Select(file => (path: PulpSource.Relative(file), text: File.ReadAllText(file)))
            .Where(hit => (HandOffForCall.IsMatch(hit.text) && hit.path is not (PlannerPath or ChildHandOffPath))
                || (ChildHandOffForCall.IsMatch(hit.text) && hit.path != PlannerPath))
            .Select(hit => hit.path)
            .ToList();

        Assert.True(offenders.Count == 0,
            "A hand-off intent is built outside HandOffPlanner and ChildHandOff:\n" + string.Join("\n", offenders));
    }
}
