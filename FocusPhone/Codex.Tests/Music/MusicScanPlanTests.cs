using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

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

    // When the reader learns to read a new field — tempo, in P6 — every cached row is
    // missing it, and no file has changed to say so. One forced pass over the library
    // fills it in; without this the new column would stay empty until each file
    // happened to be edited.
    [Fact]
    public void Compute_RereadsEverythingWhenAsked()
    {
        var version = new MusicFileVersion(10, 100);
        var current = new Dictionary<string, MusicFileVersion> { ["a"] = version, ["b"] = version };

        var plan = MusicScanPlan.Compute(current, current, rereadAll: true);

        Assert.Equal(["a", "b"], plan.ToRead.OrderBy(id => id));
    }

    // A forced pass is still a scan: files that are gone still go.
    [Fact]
    public void Compute_StillDropsMissingFilesWhenRereading()
    {
        var version = new MusicFileVersion(10, 100);
        var cached = new Dictionary<string, MusicFileVersion> { ["a"] = version, ["gone"] = version };

        var plan = MusicScanPlan.Compute(
            new Dictionary<string, MusicFileVersion> { ["a"] = version }, cached, rereadAll: true);

        Assert.Equal(["gone"], plan.ToDelete);
    }
}
