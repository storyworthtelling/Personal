using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>The numbers issued to the week's sessions, kept across app restarts: it is how a later run gives a session the key it already wears.</summary>
public sealed class FileKeyAssignmentsTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-issued-");

    public void Dispose() => folder.Delete(recursive: true);

    private string PathOf() => Path.Combine(folder.FullName, "nested", "issued-numbers.json");

    private static KeyAssignments Record() =>
        KeyAssignments.Empty.With([new KeyIssue(Sam, 105703, SessionKind.Strength, DayOfWeek.Monday, 0, 0), new KeyIssue(Sam, 105703, SessionKind.Strength, DayOfWeek.Friday, 0, 2)]);

    [Fact]
    public void NoFileIsAnEmptyRecord()
    {
        var read = new FileKeyAssignments(PathOf()).Load();

        Assert.True(read.IsReadable);
        Assert.Empty(read.Assignments.Issues);
    }

    [Fact]
    public void ARecordSurvivesTheFileAndTheFoldersAreMade()
    {
        new FileKeyAssignments(PathOf()).Save(Record());

        var back = new FileKeyAssignments(PathOf()).Load();

        Assert.True(back.IsReadable);
        Assert.Equal(Record().Issues, back.Assignments.Issues);
        Assert.Equal(KeyAssignmentsCodec.Encode(Record()), File.ReadAllText(PathOf()));
    }

    [Fact]
    public void SavingLeavesNoStagingFileBehind()
    {
        new FileKeyAssignments(PathOf()).Save(Record());

        Assert.Equal([PathOf()], Directory.GetFiles(Path.GetDirectoryName(PathOf())!));
    }

    [Fact]
    public void SavingAgainReplacesTheRecord()
    {
        var store = new FileKeyAssignments(PathOf());
        store.Save(Record());
        store.Save(Record().With([new KeyIssue(Sam, 105703, SessionKind.Strength, DayOfWeek.Wednesday, 0, 1)]));

        Assert.Equal(3, store.Load().Assignments.Issues.Count);
    }

    [Fact]
    public void AFileThatCannotBeReadIsUnreadableAndIsLeftExactlyAsItWas()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{ torn");
        var store = new FileKeyAssignments(PathOf());

        var read = store.Load();
        var again = store.Load();

        Assert.False(read.IsReadable);
        Assert.False(again.IsReadable);
        Assert.Equal("{ torn", File.ReadAllText(PathOf()));
        Assert.Equal([PathOf()], Directory.GetFiles(Path.GetDirectoryName(PathOf())!));
    }

    [Fact]
    public void ABlankPathIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new FileKeyAssignments(" "));
    }
}
