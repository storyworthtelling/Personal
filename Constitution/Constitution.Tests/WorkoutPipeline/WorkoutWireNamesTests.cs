using System.Text.Json;
using Constitution.Domain.Calendar;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// S6: the kind of a session in the delivered-pages file and the owner's-moves file is a frozen word, written out as a literal and read
/// back by exact match, never <c>Enum.ToString()</c> and <c>Enum.Parse</c>, which tie what a file on a phone says to what a member is
/// called in the source (a rename would rewrite the word, and every file already written would stop reading) and which read a number back
/// as a member. And a file set aside never replaces an earlier one set aside: the first copy may be the only record of what was lost.
/// </summary>
public sealed class WorkoutWireNamesTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-wire-");

    public void Dispose() => folder.Delete(recursive: true);

    private string PathOf(string file) => Path.Combine(folder.FullName, file);

    // The words as they stand in files already written (what Enum.ToString gave). A second copy, on purpose: moving one fails here.
    private static readonly (SessionKind Kind, string Word)[] Words =
    [
        (SessionKind.Strength, "Strength"),
        (SessionKind.OutdoorCardio, "OutdoorCardio"),
        (SessionKind.Treadmill, "Treadmill"),
        (SessionKind.Mobility, "Mobility"),
        (SessionKind.GentleWalk, "GentleWalk"),
    ];

    private static DeliveredWorkout Entry(SessionKind kind) =>
        new("Constitution — Workout — 2026-11-03 — Sam", Sam, new DateOnly(2026, 11, 3), kind, Est(11, 3, 18), "abc123", false);

    private static SessionMoved Moved(SessionKind kind) =>
        new(Sam, "c1:sam:strength:1:0", new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 5), Est(11, 5, 16), new FreeGap(Est(11, 5, 16), Est(11, 5, 18)),
            new Session(kind, TimeSpan.FromMinutes(30), false, [new Exercise("Easy walk")]));

    // ---- the words ----

    [Fact]
    public void EveryKindHasAFrozenWord()
    {
        Assert.Equal(Words.Select(pair => pair.Kind), Enum.GetValues<SessionKind>());
        Assert.All(Words, pair =>
        {
            Assert.Equal(pair.Word, WorkoutWireNames.Of(pair.Kind));
            Assert.Equal(pair.Kind, WorkoutWireNames.SessionKindOf(pair.Word));
        });
    }

    [Theory]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("strength")]
    [InlineData("STRENGTH")]
    [InlineData("Strength,Mobility")]
    [InlineData(" Strength")]
    [InlineData("")]
    [InlineData(null)]
    public void OnlyTheExactWordIsAKind(string? text)
    {
        Assert.Null(WorkoutWireNames.SessionKindOf(text));
    }

    [Fact]
    public void AKindNobodyNamedHasNoWord()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkoutWireNames.Of((SessionKind)99));
    }

    [Fact]
    public void TheDeliveredFileWritesTheFrozenWord()
    {
        foreach (var (kind, word) in Words)
        {
            var path = PathOf($"delivered-{word}.json");
            new FileDeliveredWorkouts(path).Record(Entry(kind));

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(word, document.RootElement.GetProperty("delivered")[0].GetProperty("kind").GetString());
        }
    }

    [Fact]
    public void TheMovesFileWritesTheFrozenWord()
    {
        foreach (var (kind, word) in Words)
        {
            var text = PlanOverridesCodec.Encode(new PlanOverrides([new MoveGroup([Moved(kind)])], []));

            using var document = JsonDocument.Parse(text);
            Assert.Equal(word, document.RootElement.GetProperty("moves")[0][0].GetProperty("kind").GetString());
            Assert.Equal(kind, PlanOverridesCodec.Decode(text).Groups.Single().Moves.Single().Session.Kind);
        }
    }

    [Theory]
    [InlineData("3")]
    [InlineData("strength")]
    [InlineData("Swimming")]
    public void ANumberOrAStrangeWordIsNotAKindInTheDeliveredFile(string word)
    {
        var path = PathOf("delivered.json");
        new FileDeliveredWorkouts(path).Record(Entry(SessionKind.Mobility));
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"Mobility\"", $"\"{word}\"", StringComparison.Ordinal));

        Assert.Null(new FileDeliveredWorkouts(path).Find(Entry(SessionKind.Mobility).Name));
        Assert.True(File.Exists(path + ".unreadable"));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("mobility")]
    [InlineData("Swimming")]
    public void ANumberOrAStrangeWordIsNotAKindInTheMovesFile(string word)
    {
        var text = PlanOverridesCodec.Encode(new PlanOverrides([new MoveGroup([Moved(SessionKind.Mobility)])], []))
            .Replace("\"Mobility\"", $"\"{word}\"", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => PlanOverridesCodec.Decode(text));
    }

    [Fact]
    public void NeitherFileUsesTheEnumsOwnSpelling()
    {
        foreach (var name in new[] { "FileDeliveredWorkouts.cs", "FileWorkoutOverrides.cs" })
        {
            var file = ConstitutionSource.Files(".cs").Single(path => path.EndsWith("Infrastructure/Workouts/" + name, StringComparison.Ordinal));
            var text = File.ReadAllText(file);

            Assert.DoesNotContain("Enum.Parse", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Enum.TryParse", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Kind.ToString()", text, StringComparison.Ordinal);
        }
    }

    // ---- a file set aside ----

    [Fact]
    public void ASecondUnreadableDeliveredFileKeepsTheFirstCopy()
    {
        var path = PathOf("delivered.json");
        File.WriteAllText(path, "{ the first damage");
        Assert.Null(new FileDeliveredWorkouts(path).Find("anything"));
        File.WriteAllText(path, "{ the second damage");

        Assert.Null(new FileDeliveredWorkouts(path).Find("anything"));

        Assert.Equal("{ the first damage", File.ReadAllText(path + ".unreadable"));
        Assert.Equal("{ the second damage", File.ReadAllText(path + ".unreadable.2"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ASecondUnreadableMovesFileKeepsTheFirstCopy()
    {
        var path = PathOf("overrides.json");
        File.WriteAllText(path, "{ the first damage");
        Assert.True(new FileWorkoutOverrides(path).Load().IsEmpty);
        File.WriteAllText(path, "{ the second damage");

        Assert.True(new FileWorkoutOverrides(path).Load().IsEmpty);

        Assert.Equal("{ the first damage", File.ReadAllText(path + ".unreadable"));
        Assert.Equal("{ the second damage", File.ReadAllText(path + ".unreadable.2"));
    }

    [Fact]
    public void ManyUnreadableFilesAreAllKept()
    {
        var path = PathOf("delivered.json");
        foreach (var round in Enumerable.Range(1, 4))
        {
            File.WriteAllText(path, $"{{ damage {round}");
            Assert.Null(new FileDeliveredWorkouts(path).Find("anything"));
        }

        Assert.Equal(
            ["{ damage 1", "{ damage 2", "{ damage 3", "{ damage 4"],
            new[] { ".unreadable", ".unreadable.2", ".unreadable.3", ".unreadable.4" }.Select(suffix => File.ReadAllText(path + suffix)));
    }
}
