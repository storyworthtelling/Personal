using Pulp.Domain.Children;
using Pulp.Domain.HandOffs;

namespace Pulp.Tests.HandOff;

/// <summary>
/// The executor's plain-language notices for the dog-ear (D122). Success has none — it is the service's
/// app opening — and every refusal and failure has one, naming the service and what to do next.
/// </summary>
public class HandOffNoticeTests
{
    [Fact]
    public void EveryRefusalHasASentenceAndNoneIsEmpty()
    {
        var refusals = Enum.GetValues<StartRefusal>().Where(refusal => refusal != StartRefusal.None).ToList();
        Assert.NotEmpty(refusals);

        foreach (var refusal in refusals)
        {
            var notice = HandOffNotice.ForRefusal(refusal, "Exampleflix");
            Assert.False(string.IsNullOrWhiteSpace(notice));
            Assert.EndsWith(".", notice);
        }
    }

    [Fact]
    public void ARefusalThatIsNotOneIsAProgrammingError() =>
        Assert.Throws<ArgumentException>(() => HandOffNotice.ForRefusal(StartRefusal.None, "Exampleflix"));

    [Fact]
    public void TheUnattestedNoticeSaysWhatToDoNext()
    {
        var notice = HandOffNotice.ForRefusal(StartRefusal.ServiceNotAttested, "Exampleflix");

        Assert.Contains("Exampleflix", notice);
        Assert.Contains("Settings", notice);
    }

    [Fact]
    public void TheNotInstalledNoticeNamesTheService()
    {
        var notice = HandOffNotice.NotInstalled("Exampleflix");

        Assert.Contains("Exampleflix", notice);
        Assert.Contains("install", notice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMisLandingNoticeNamesTheService() =>
        Assert.Contains("Exampleflix", HandOffNotice.LandedElsewhere("Exampleflix"));

    // D286: nothing played after the hand-off — a hedged sentence, never an alarm.
    [Fact]
    public void ANotSeenNoticeNamesTheServiceAndHedges()
    {
        var notice = HandOffNotice.NotSeen("Exampleflix");

        Assert.Contains("Exampleflix", notice);
        Assert.Contains("may not", notice);
    }
}
