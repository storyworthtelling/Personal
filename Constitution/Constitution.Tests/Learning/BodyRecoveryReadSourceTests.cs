using Constitution.Domain.Body;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.Workouts;

namespace Constitution.Tests.Learning;

public class BodyRecoveryReadSourceTests
{
    private static readonly DateOnly Morning = new(2026, 11, 4);

    private static JsonBodyStore Store(LearningFolder folder) => new(folder.File("body.json"));

    private static IReadOnlyList<BodyReading> Readings() =>
        Enumerable.Range(0, 15)
            .SelectMany(back => new[]
            {
                new BodyReading(BodySignal.Sleep, Morning.AddDays(-back), back == 0 ? 4.0 : 7.5),
                new BodyReading(BodySignal.HeartRateVariability, Morning.AddDays(-back), back == 0 ? 30 : 50),
                new BodyReading(BodySignal.RestingHeartRate, Morning.AddDays(-back), back == 0 ? 72 : 60),
            })
            .ToList();

    [Fact]
    public void AnEmptyBodyLeavesTheOwnerAbsent()
    {
        using var folder = new LearningFolder();

        var read = new BodyRecoveryReadSource("Sam", Store(folder)).Read(["Sam", "Lee"], Morning);

        Assert.Empty(read);
    }

    [Fact]
    public void OnlyTheOwnersBodyIsOnThisPhone()
    {
        using var folder = new LearningFolder();
        var store = Store(folder);
        store.Upsert(Readings());

        var read = new BodyRecoveryReadSource("Sam", store).Read(["sam", "Lee"], Morning);

        Assert.Equal(["sam"], read.Keys);
    }

    [Fact]
    public void TheReadIsTheReadersOwnAnswer()
    {
        using var folder = new LearningFolder();
        var store = Store(folder);
        store.Upsert(Readings());

        var read = new BodyRecoveryReadSource("Sam", store).Read(["Sam"], Morning);

        var expected = RecoveryReader.Read(store.All(), Morning);
        Assert.Equal(expected.State, read["Sam"].State);
        Assert.Equal(expected.Reasons, read["Sam"].Reasons);
        Assert.NotEqual(RecoveryState.NotEnoughToSay, read["Sam"].State);
    }

    [Fact]
    public void NobodyAskedIsNobodyRead()
    {
        using var folder = new LearningFolder();
        var store = Store(folder);
        store.Upsert(Readings());

        Assert.Empty(new BodyRecoveryReadSource("Sam", store).Read([], Morning));
    }
}
