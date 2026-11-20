using Constitution.Tests.HouseholdHead;
using Storyworthtelling.Household;

namespace Constitution.Tests.ForewordHousehold;

/// <summary>
/// The household mirror runs after every pass and usually changes nothing. An edit that changes nothing must not save or ask for a
/// pass, or each pass would ask for the next one, for ever.
/// </summary>
public class UnchangedEditTests
{
    [Fact]
    public async Task AnEditThatChangesNothingAsksForNoPass()
    {
        using var rig = new HeadRig();
        var asked = 0;
        rig.Service.SyncWanted += () => asked++;

        await rig.Service.EditAsync((HouseholdDocument document, HybridLogicalClock clock, string writer) => document, CancellationToken.None);

        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task AnEditThatChangesSomethingStillAsks()
    {
        using var rig = new HeadRig();
        var asked = 0;
        rig.Service.SyncWanted += () => asked++;

        await rig.Service.EditAsync(
            (document, clock, writer) => document.With(HouseholdRecord.Write(
                new RecordKey("example-kind", "one"), 1, new Dictionary<string, string> { ["field"] = "value" }, clock.Now(1_000), writer)),
            CancellationToken.None);

        Assert.Equal(1, asked);
    }
}
