using Storyworthtelling.CallSheet;

namespace Storyworthtelling.CallSheet.Tests;

/// <summary>Where a head keeps what each seat was last given, so a line survives a restart (audit W10.3).</summary>
public class SeatMemoryFileTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("seat-memory-").FullName;
    private string Path => System.IO.Path.Combine(folder, "voice-seats.json");
    private static readonly DateTimeOffset Now = new(2026, 3, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly VoiceLine Seat = CallSheet.For("Pulp", "child-greeting");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void ALineIsReadBackByANewInstance()
    {
        new SeatMemoryFile(Path).Put(Seat, "robin", SeatPlan.Remember("h", new DateOnly(2026, 3, 9), "Hello.", Now));

        var read = new SeatMemoryFile(Path).Get(Seat, "robin");

        Assert.Equal("Hello.", read?.Line);
        Assert.Equal("h", read?.FactsHash);
    }

    /// <summary>Two children on one phone keep their own lines (audit 06: one child's line overwrote another's).</summary>
    [Fact]
    public void EachSubjectKeepsItsOwnLine()
    {
        var file = new SeatMemoryFile(Path);
        file.Put(Seat, "robin", SeatPlan.Remember("a", new DateOnly(2026, 3, 9), "Hi Robin.", Now));
        file.Put(Seat, "juniper", SeatPlan.Remember("b", new DateOnly(2026, 3, 9), "Hi Juniper.", Now));

        Assert.Equal("Hi Robin.", file.Get(Seat, "robin")?.Line);
        Assert.Equal("Hi Juniper.", file.Get(Seat, "juniper")?.Line);
    }

    [Fact]
    public void AMissingOrDamagedFileRemembersNothing()
    {
        Assert.Null(new SeatMemoryFile(Path).Get(Seat));

        File.WriteAllText(Path, "{ not json");
        Assert.Null(new SeatMemoryFile(Path).Get(Seat));
    }

    [Fact]
    public void TheFileKeepsOnlyTheNewest()
    {
        var file = new SeatMemoryFile(Path);
        for (var i = 0; i < SeatMemoryFile.Entries + 5; i++)
        {
            file.Put(Seat, $"subject{i}", SeatPlan.Remember("h", new DateOnly(2026, 3, 9), "Hi.", Now));
        }

        Assert.Null(file.Get(Seat, "subject0"));
        Assert.NotNull(file.Get(Seat, $"subject{SeatMemoryFile.Entries + 4}"));
    }
}
