using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>The owner's swaps, moves and skips on disk: small, in order, and never able to stop the morning if the file is bad.</summary>
public sealed class FileWorkoutOverridesTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-overrides-");

    public void Dispose() => folder.Delete(recursive: true);

    private string PathOf() => Path.Combine(folder.FullName, "nested", "overrides.json");

    private FileWorkoutOverrides Make() => new(PathOf());

    private static SessionMoved Moved(int fromDay = 4, int day = 6, string key = "c1:sam:mobility:2947:0", string person = "Sam", bool sweaty = false) =>
        new(
            person, key, new DateOnly(2026, 11, fromDay), new DateOnly(2026, 11, day), Est(11, day, 16, 30),
            new FreeGap(Est(11, day, 16), Est(11, day, 18)),
            new Session(SessionKind.Mobility, TimeSpan.FromMinutes(20), sweaty, [new Exercise("Cat and cow", EquipmentCatalogue.MatId), new Exercise("Calf stretch")]));

    private static PlanOverrides Group(params SessionMoved[] moves) => new([new MoveGroup(moves)], []);

    private static PlanOverrides Skip(int day = 5, string person = "Sam") => new([], [new SessionSkipped(person, new DateOnly(2026, 11, day))]);

    [Fact]
    public void NothingKeptIsNothing()
    {
        var read = Make().Load();

        Assert.True(read.IsEmpty);
    }

    [Fact]
    public void AMoveSurvivesARestart()
    {
        var moved = Moved();
        Make().Add(Group(moved));

        var back = Assert.Single(Make().Load().Groups).Moves.Single();

        Assert.Equal(moved.Person, back.Person);
        Assert.Equal(moved.Key, back.Key);
        Assert.Equal(moved.FromDay, back.FromDay);
        Assert.Equal(moved.Day, back.Day);
        Assert.Equal(moved.Start, back.Start);
        Assert.Equal(moved.Gap, back.Gap);
        Assert.Equal(TimeSpan.FromHours(-5), back.Start.Offset);
        Assert.Equal(moved.Session.Kind, back.Session.Kind);
        Assert.Equal(moved.Session.Duration, back.Session.Duration);
        Assert.Equal(moved.Session.IsSweaty, back.Session.IsSweaty);
        Assert.Equal(moved.Session.Exercises, back.Session.Exercises);
    }

    [Fact]
    public void ASweatySessionStaysSweaty()
    {
        Make().Add(Group(Moved(sweaty: true)));

        Assert.True(Assert.Single(Make().Load().Groups).Moves.Single().Session.IsSweaty);
    }

    [Fact]
    public void ASwapIsOneGroupAndStaysOne()
    {
        Make().Add(Group(Moved(4, 6), Moved(6, 4, "c1:sam:mobility:2947:1")));

        var group = Assert.Single(Make().Load().Groups);

        Assert.Equal(2, group.Moves.Count);
    }

    [Fact]
    public void WhatIsAddedComesBackInTheOrderItWasMade()
    {
        var store = Make();
        store.Add(Group(Moved(4, 6)));
        store.Add(Skip(5));
        store.Add(Group(Moved(7, 8, "c1:sam:mobility:2947:1")));

        var read = Make().Load();

        Assert.Equal(["c1:sam:mobility:2947:0", "c1:sam:mobility:2947:1"], read.Groups.Select(group => group.Moves[0].Key));
        Assert.Equal(new DateOnly(2026, 11, 5), Assert.Single(read.Skips).Day);
    }

    [Fact]
    public void ASkipRecordedTwiceIsKeptOnce()
    {
        var store = Make();
        store.Add(Skip(5));
        store.Add(Skip(5));

        Assert.Single(Make().Load().Skips);
    }

    [Fact]
    public void AnUnreadableFileIsSetAside()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{ this is not the file");

        var store = Make();

        Assert.True(store.Load().IsEmpty);
        Assert.True(File.Exists(PathOf() + ".unreadable"));
        store.Add(Skip(5));
        Assert.Single(Make().Load().Skips);
    }

    [Fact]
    public void AnUnknownKindMakesItUnreadable()
    {
        var store = Make();
        store.Add(Group(Moved()));
        File.WriteAllText(PathOf(), File.ReadAllText(PathOf()).Replace("Mobility", "Swimming", StringComparison.Ordinal));

        Assert.True(Make().Load().IsEmpty);
        Assert.True(File.Exists(PathOf() + ".unreadable"));
    }

    [Fact]
    public void TheFileHoldsNoPageContent()
    {
        Make().Add(Group(Moved()));

        var text = File.ReadAllText(PathOf());

        Assert.DoesNotContain("dose", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("encourage", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheCodecRoundTripsTheSameText()
    {
        var overrides = Group(Moved()).Then(Skip(5));

        var text = PlanOverridesCodec.Encode(overrides);

        Assert.Equal(text, PlanOverridesCodec.Encode(PlanOverridesCodec.Decode(text)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"moves\":[],\"skips\":[]}")]
    [InlineData("{\"version\":1}")]
    public void ATextThatIsNotARecordIsRefusedByName(string text)
    {
        Assert.Throws<FormatException>(() => PlanOverridesCodec.Decode(text));
    }
}
