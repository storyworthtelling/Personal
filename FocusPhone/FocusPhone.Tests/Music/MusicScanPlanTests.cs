using FocusPhone.Domain.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class MusicScanPlanTests
{
    private static MusicFileVersion V(long ticks, long size = 100) => new(ticks, size);

    [Fact]
    public void NewFile_IsRead()
    {
        var plan = MusicScanPlan.Compute(
            current: new Dictionary<string, MusicFileVersion> { ["a"] = V(1) },
            cached: new Dictionary<string, MusicFileVersion>());

        Assert.Equal(["a"], plan.ToRead);
        Assert.Empty(plan.ToDelete);
    }

    [Fact]
    public void UnchangedFile_IsSkipped()
    {
        var same = V(1);
        var plan = MusicScanPlan.Compute(
            current: new Dictionary<string, MusicFileVersion> { ["a"] = same },
            cached: new Dictionary<string, MusicFileVersion> { ["a"] = same });

        Assert.Empty(plan.ToRead);
        Assert.Empty(plan.ToDelete);
    }

    [Fact]
    public void ChangedModifiedTime_IsRead()
    {
        var plan = MusicScanPlan.Compute(
            current: new Dictionary<string, MusicFileVersion> { ["a"] = V(2) },
            cached: new Dictionary<string, MusicFileVersion> { ["a"] = V(1) });

        Assert.Equal(["a"], plan.ToRead);
    }

    [Fact]
    public void ChangedSize_IsRead()
    {
        var plan = MusicScanPlan.Compute(
            current: new Dictionary<string, MusicFileVersion> { ["a"] = V(1, size: 200) },
            cached: new Dictionary<string, MusicFileVersion> { ["a"] = V(1, size: 100) });

        Assert.Equal(["a"], plan.ToRead);
    }

    [Fact]
    public void MissingFromDrive_IsDeleted()
    {
        var plan = MusicScanPlan.Compute(
            current: new Dictionary<string, MusicFileVersion>(),
            cached: new Dictionary<string, MusicFileVersion> { ["gone"] = V(1) });

        Assert.Empty(plan.ToRead);
        Assert.Equal(["gone"], plan.ToDelete);
    }

    [Fact]
    public void MixedLibrary_PartitionsCorrectly()
    {
        var stable = V(5);
        var plan = MusicScanPlan.Compute(
            current: new Dictionary<string, MusicFileVersion>
            {
                ["keep"] = stable,          // unchanged
                ["edited"] = V(9),          // changed
                ["new"] = V(1),             // added
            },
            cached: new Dictionary<string, MusicFileVersion>
            {
                ["keep"] = stable,
                ["edited"] = V(2),
                ["removed"] = V(3),         // deleted
            });

        Assert.Equal(["edited", "new"], plan.ToRead.OrderBy(id => id).ToArray());
        Assert.Equal(["removed"], plan.ToDelete);
    }
}
