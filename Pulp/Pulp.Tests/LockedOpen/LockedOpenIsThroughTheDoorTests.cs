using System.Text.RegularExpressions;

namespace Pulp.Tests.LockedOpen;

/// <summary>
/// G13 and G8 for Release G, structurally. A locked hand-off is a hand-off: it must not be a second
/// door past the child gate. The request is built only inside <c>HandOffExecutor</c>, from the
/// planner's own intent, after the plan has allowed it; nothing else in the head names the courier.
/// Read by content, because the head is not compiled in this suite.
/// </summary>
public class LockedOpenIsThroughTheDoorTests
{
    private const string ExecutorPath = "Pulp.Android/HandOffs/HandOffExecutor.cs";
    private const string CourierPath = "Pulp.Android/LockedOpen/LockedOpenCourier.cs";

    private static string Executor() => File.ReadAllText(Path.Combine(PulpSource.Root(), ExecutorPath));

    [Fact]
    public void OnlyTheExecutorSendsALockedOpen()
    {
        var files = PulpSource.Files(".cs").Where(file => PulpSource.Relative(file).StartsWith("Pulp.Android/", StringComparison.Ordinal)).ToList();
        Assert.Contains(files, file => PulpSource.Relative(file) == CourierPath);

        var offenders = files
            .Where(file => PulpSource.Relative(file) is not (ExecutorPath or CourierPath))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\bLockedOpenCourier\s*\.\s*Send\b|\bLockedOpenHandOff\s*\.\s*Extras\b"))
            .Select(PulpSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "A locked open was sent outside HandOffExecutor, past the child gate:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheLockedSendComesAfterThePlanAllowsItAndUsesOnlyThePlansIntent()
    {
        var text = Executor();
        var refused = text.IndexOf("HandOffResult.Refused", StringComparison.Ordinal);
        var send = text.IndexOf("LockedOpenCourier.Send(", StringComparison.Ordinal);
        var plain = text.IndexOf("host.StartActivity(intent)", StringComparison.Ordinal);

        Assert.True(refused >= 0 && send > refused, "The locked send must come after the refusal return.");
        Assert.True(plain > send, "The plain hand-off is the fallback after the locked one.");
        Assert.Contains("LockedOpenCourier.Send(host, plan.Intent.Package, plan.Intent.Data)", text, StringComparison.Ordinal);
        Assert.Contains("LockedOpenChoice.Decide(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedLockedSendFallsBackToThePlainHandOff()
    {
        var text = Executor();

        Assert.Matches(@"HandOffRoute\.Locked\s*&&\s*LockedOpenCourier\.Send\(", text);
    }

    [Fact]
    public void TheCourierIsNotAViewIntentBuilder()
    {
        var courier = File.ReadAllText(Path.Combine(PulpSource.Root(), CourierPath));

        Assert.DoesNotMatch(@"\bActionView\b|android\.intent\.action\.VIEW|\bHandOffIntent\b|\bHandOffPlanner\b|\bSetPackage\s*\(", courier);
        Assert.DoesNotMatch(@"(?<![\w.])HandOff\s*\.\s*For\s*\(|\bChildHandOff\s*\.\s*For\s*\(", courier);
    }
}
