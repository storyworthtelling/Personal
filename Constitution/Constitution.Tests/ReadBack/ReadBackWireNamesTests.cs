using Constitution.Domain.Delivery;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Documents;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.ReadBack;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// The words the read-back's files use for the closed lists, typed out here as literals on purpose (review finding S6).
/// They were <c>Enum.ToString()</c> and <c>Enum.Parse</c>, so renaming a member renamed what the file said, and a number
/// or a word of another case read back as a member. A file on a phone does not follow a refactor: these fail instead.
/// </summary>
public sealed class ReadBackWireNamesTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-wire-");

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public void TheDeliveryKindsAreFrozen()
    {
        Assert.Equal(
            [("Workout", DeliveryKind.Workout), ("Meals", DeliveryKind.Meals), ("Digest", DeliveryKind.Digest), ("Recipe", DeliveryKind.Recipe)],
            Enum.GetValues<DeliveryKind>().Select(kind => (ReadBackWireNames.Of(kind), kind)).ToArray());
        Assert.All(Enum.GetValues<DeliveryKind>(), kind => Assert.Equal(kind, ReadBackWireNames.DeliveryKindOf(ReadBackWireNames.Of(kind))));
    }

    [Fact]
    public void TheSessionKindsAreFrozen()
    {
        Assert.Equal(
            ["Strength", "OutdoorCardio", "Treadmill", "Mobility", "GentleWalk"],
            Enum.GetValues<SessionKind>().Select(ReadBackWireNames.Of).ToArray());
        Assert.All(Enum.GetValues<SessionKind>(), kind => Assert.Equal(kind, ReadBackWireNames.SessionKindOf(ReadBackWireNames.Of(kind))));
    }

    [Fact]
    public void TheBoxKindsAreFrozen()
    {
        Assert.Equal(
            ["LargeTick", "StandardTick", "CircleLane", "NumberBox", "NoBecause", "NotesMargin"],
            Enum.GetValues<InkTargetKind>().Select(ReadBackWireNames.Of).ToArray());
        Assert.All(Enum.GetValues<InkTargetKind>(), kind => Assert.Equal(kind, ReadBackWireNames.InkTargetKindOf(ReadBackWireNames.Of(kind))));
    }

    [Fact]
    public void TheDiagnosticsOutcomesAreFrozen()
    {
        Assert.Equal(
            ["NeverRan", "Succeeded", "Refused", "Unreachable", "FoundNothingRead", "Partial"],
            Enum.GetValues<DiagnosticsOutcome>().Select(ReadBackWireNames.Of).ToArray());
        Assert.All(Enum.GetValues<DiagnosticsOutcome>(), kind => Assert.Equal(kind, ReadBackWireNames.DiagnosticsOutcomeOf(ReadBackWireNames.Of(kind))));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("workout")]
    [InlineData("WORKOUT")]
    [InlineData("Workout, Meals")]
    [InlineData(" Workout")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("99")]
    public void ANumberAnotherCaseOrAListIsNotAKind(string? name)
    {
        Assert.Null(ReadBackWireNames.DeliveryKindOf(name));
        Assert.Null(ReadBackWireNames.SessionKindOf(name));
        Assert.Null(ReadBackWireNames.InkTargetKindOf(name));
        Assert.Null(ReadBackWireNames.DiagnosticsOutcomeOf(name));
    }

    [Fact]
    public void AValueOutsideTheListHasNoWordToWrite()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadBackWireNames.Of((DeliveryKind)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadBackWireNames.Of((SessionKind)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadBackWireNames.Of((InkTargetKind)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadBackWireNames.Of((DiagnosticsOutcome)42));
    }

    // ---- through the files ----

    private string ExpectedPath() => Path.Combine(folder.FullName, "expected.json");

    [Fact]
    public void TheManifestWritesTheFrozenWords()
    {
        new FileExpectedDocuments(ExpectedPath()).Record(Workout());

        var text = File.ReadAllText(ExpectedPath());

        Assert.Contains("\"kind\":\"Workout\"", text, StringComparison.Ordinal);
        Assert.Contains("\"session\":\"Strength\"", text, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"LargeTick\"", text, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"CircleLane\"", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"kind\":\"Workout\"", "\"kind\":\"0\"")]
    [InlineData("\"kind\":\"Workout\"", "\"kind\":\"workout\"")]
    [InlineData("\"session\":\"Strength\"", "\"session\":\"0\"")]
    [InlineData("\"kind\":\"LargeTick\"", "\"kind\":\"0\"")]
    public void AManifestWithAWordOffTheListIsSetAside(string from, string to)
    {
        new FileExpectedDocuments(ExpectedPath()).Record(Workout());
        File.WriteAllText(ExpectedPath(), File.ReadAllText(ExpectedPath()).Replace(from, to, StringComparison.Ordinal));

        Assert.Empty(new FileExpectedDocuments(ExpectedPath()).Outstanding());
        Assert.True(File.Exists(ExpectedPath() + ".unreadable"));
    }

    [Fact]
    public void AResultWritesTheFrozenWordAndRefusesAnother()
    {
        var store = new FileResolutionStore(Path.Combine(folder.FullName, "results"));
        var result = new DocumentResult(Workout().Name, DeliveryKind.Workout, Day, Person, Morning, [new PageResolutions(2, [new Constitution.Domain.Ink.InkResolution.Ticked("done")])]);
        store.Save(result);
        var file = Directory.GetFiles(Path.Combine(folder.FullName, "results"), "*.json").Single();
        Assert.Contains("\"kind\":\"Workout\"", File.ReadAllText(file), StringComparison.Ordinal);

        File.WriteAllText(file, File.ReadAllText(file).Replace("\"kind\":\"Workout\"", "\"kind\":\"0\"", StringComparison.Ordinal));

        Assert.Null(store.Load(result.Name));
        Assert.True(File.Exists(file + ".unreadable"));
    }

    [Fact]
    public void TheStatusFileWritesTheFrozenWordAndReadsOnlyThat()
    {
        var path = Path.Combine(folder.FullName, "status.json");
        var status = new ReadBackStatusFile(path, () => Morning);
        status.Record(DiagnosticsOutcome.FoundNothingRead);

        Assert.Contains("\"outcome\":\"FoundNothingRead\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(DiagnosticsOutcome.FoundNothingRead, status.LastOutcome());

        File.WriteAllText(path, "{\"outcome\":\"4\",\"at\":\"2026-11-04T07:15:00-05:00\"}");

        Assert.Equal(DiagnosticsOutcome.NeverRan, status.LastOutcome());
    }

    // ---- a second failure never destroys the first ----

    [Fact]
    public void ASecondUnreadableFileDoesNotOverwriteTheFirst()
    {
        var path = ExpectedPath();
        File.WriteAllText(path, "{ torn the first time");
        Assert.Empty(new FileExpectedDocuments(path).Outstanding());
        new FileExpectedDocuments(path).Record(Workout());
        File.WriteAllText(path, "{ torn the second time");

        Assert.Empty(new FileExpectedDocuments(path).Outstanding());

        Assert.Equal("{ torn the first time", File.ReadAllText(path + ".unreadable"));
        var copies = Directory.GetFiles(folder.FullName, "expected.json.unreadable*");
        Assert.Equal(2, copies.Length);
        Assert.Contains(copies, copy => File.ReadAllText(copy) == "{ torn the second time");
    }

    [Fact]
    public void ManyUnreadableFilesAreAllKept()
    {
        var path = ExpectedPath();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            File.WriteAllText(path, "{ torn " + attempt);
            Assert.Empty(new FileExpectedDocuments(path).Outstanding());
        }

        var kept = Directory.GetFiles(folder.FullName, "expected.json.unreadable*").Select(File.ReadAllText).Order().ToArray();
        Assert.Equal(["{ torn 1", "{ torn 2", "{ torn 3", "{ torn 4"], kept);
    }
}
