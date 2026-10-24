using Constitution.Domain.Calendar;
using Constitution.Infrastructure.Workouts;

namespace Constitution.Tests.HouseholdSettings;

public sealed class PlanningSetupWriteTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("setup-write");

    public void Dispose() => folder.Delete(recursive: true);

    private string PathOf() => Path.Combine(folder.FullName, "workout-week", "planning-setup.json");

    [Fact]
    public void WritesAFileTheRunReads()
    {
        var result = PlanningSetupFile.Write(PathOf(), HouseholdData.Setup());

        Assert.Equal(PlanningSetupWrite.Written, result);
        var read = Assert.IsType<HouseholdSetupRead.Configured>(PlanningSetupFile.Read(PathOf()));
        Assert.Equal(["Sam"], read.Setup.DeliverFor);
    }

    [Fact]
    public void ReplacesAReadableFileAndLeavesNoTemporary()
    {
        PlanningSetupFile.Write(PathOf(), HouseholdData.Setup());
        PlanningSetupFile.Write(PathOf(), HouseholdData.Setup() with { Bedtime = new TimeOnly(20, 0) });

        var read = Assert.IsType<HouseholdSetupRead.Configured>(PlanningSetupFile.Read(PathOf()));
        Assert.Equal(new TimeOnly(20, 0), read.Setup.Bedtime);
        Assert.Equal([PathOf()], Directory.GetFiles(Path.GetDirectoryName(PathOf())!));
    }

    [Fact]
    public void AnUnreadableFileIsSetAsideNotOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{ not json");

        var result = PlanningSetupFile.Write(PathOf(), HouseholdData.Setup());

        Assert.Equal(PlanningSetupWrite.WrittenAfterSettingAsideAnUnreadableFile, result);
        Assert.Equal("{ not json", File.ReadAllText(PathOf() + ".unreadable"));
        Assert.IsType<HouseholdSetupRead.Configured>(PlanningSetupFile.Read(PathOf()));
    }

    [Fact]
    public void ADirectoryInTheWayIsAnErrorNotADeletion()
    {
        Directory.CreateDirectory(PathOf());

        var failure = Assert.Throws<IOException>(() => PlanningSetupFile.Write(PathOf(), HouseholdData.Setup()));

        Assert.Contains("directory", failure.Message);
        Assert.True(Directory.Exists(PathOf()));
    }
}
