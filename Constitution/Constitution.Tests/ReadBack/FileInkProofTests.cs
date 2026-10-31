using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>What the read-back has seen of pen ink: remembered across runs, and safe to lose.</summary>
public sealed class FileInkProofTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly DateOnly Tuesday = new(2026, 11, 3);

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-proof-");

    private string PathOf() => Path.Combine(folder.FullName, "nested", "ink-proof.json");

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public void NothingSeenAtFirst()
    {
        Assert.False(new FileInkProof(PathOf()).Current.IsEstablished);
        Assert.Empty(new FileInkProof(PathOf()).Current.Sightings);
    }

    [Fact]
    public void OneSightingIsRememberedButIsNotProof()
    {
        new FileInkProof(PathOf()).Record(new InkSighting("Workout A", Monday));

        var current = new FileInkProof(PathOf()).Current;

        Assert.False(current.IsEstablished);
        Assert.Equal([new InkSighting("Workout A", Monday)], current.Sightings.ToArray());
    }

    [Fact]
    public void TwoSightingsOnTwoMorningsAreProofForEveryLaterRun()
    {
        new FileInkProof(PathOf()).Record(new InkSighting("Workout A", Monday));
        var returned = new FileInkProof(PathOf()).Record(new InkSighting("Workout B", Tuesday));

        var current = new FileInkProof(PathOf()).Current;

        Assert.True(returned.IsEstablished);
        Assert.True(current.IsEstablished);
        Assert.Equal(Tuesday, current.EstablishedOn);
    }

    [Fact]
    public void RecordingTheSameSightingTwiceChangesNothing()
    {
        var proof = new FileInkProof(PathOf());
        proof.Record(new InkSighting("Workout A", Monday));
        var before = File.ReadAllText(PathOf());

        proof.Record(new InkSighting("Workout A", Monday));

        Assert.Equal(before, File.ReadAllText(PathOf()));
    }

    [Fact]
    public void AFileFromBeforeSightingsWereKeptIsEstablishedWithNoDate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{\"penMarksSeen\":true}");

        var current = new FileInkProof(PathOf()).Current;

        Assert.True(current.IsEstablished);
        Assert.Null(current.EstablishedOn);
    }

    [Fact]
    public void ADamagedFileReadsAsNotSeenAndTheNextRecordWritesAGoodOne()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{ not json");
        var proof = new FileInkProof(PathOf());

        Assert.False(proof.Current.IsEstablished);
        proof.Record(new InkSighting("Workout A", Monday));

        Assert.Single(new FileInkProof(PathOf()).Current.Sightings);
    }

    [Fact]
    public void AFileSayingNoIsNotSeen()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{\"penMarksSeen\":false}");

        Assert.False(new FileInkProof(PathOf()).Current.IsEstablished);
    }

    [Fact]
    public void ADateThatCannotBeReadMakesItNotSeenRatherThanProven()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{\"penMarksSeen\":true,\"establishedOn\":\"someday\"}");

        Assert.False(new FileInkProof(PathOf()).Current.IsEstablished);
    }

    [Fact]
    public void ASightingThatCannotBeReadIsLeftOut()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{\"penMarksSeen\":false,\"sightings\":[{\"document\":\"Workout A\",\"morning\":\"2026-11-02\"},{\"document\":\"Workout B\",\"morning\":\"soon\"},{\"morning\":\"2026-11-03\"}]}");

        var current = new FileInkProof(PathOf()).Current;

        Assert.False(current.IsEstablished);
        Assert.Equal([new InkSighting("Workout A", Monday)], current.Sightings.ToArray());
    }

    [Fact]
    public void TheStoredNamesAreLiteralsAndPinned()
    {
        var proof = new FileInkProof(PathOf());
        proof.Record(new InkSighting("Workout A", Monday));
        var text = File.ReadAllText(PathOf());
        Assert.Contains("\"penMarksSeen\":false", text, StringComparison.Ordinal);
        Assert.Contains("\"sightings\":[{\"document\":\"Workout A\",\"morning\":\"2026-11-02\"}]", text, StringComparison.Ordinal);

        proof.Record(new InkSighting("Workout B", Tuesday));

        var established = File.ReadAllText(PathOf());
        Assert.Contains("\"penMarksSeen\":true", established, StringComparison.Ordinal);
        Assert.Contains("\"establishedOn\":\"2026-11-03\"", established, StringComparison.Ordinal);
    }
}
