using Constitution.Infrastructure.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>Whether pen ink has ever come through the cloud's PDF: remembered across runs, and safe to lose.</summary>
public sealed class FileInkProofTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-proof-");

    private string PathOf() => Path.Combine(folder.FullName, "nested", "ink-proof.json");

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public void NothingSeenAtFirst() => Assert.False(new FileInkProof(PathOf()).PenMarksSeen);

    [Fact]
    public void OnceSeenItIsSeenByEveryLaterRun()
    {
        new FileInkProof(PathOf()).RecordPenMarksSeen();

        Assert.True(new FileInkProof(PathOf()).PenMarksSeen);
    }

    [Fact]
    public void RecordingTwiceChangesNothing()
    {
        var proof = new FileInkProof(PathOf());
        proof.RecordPenMarksSeen();
        proof.RecordPenMarksSeen();

        Assert.True(proof.PenMarksSeen);
    }

    [Fact]
    public void ADamagedFileReadsAsNotSeen()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{ not json");
        var proof = new FileInkProof(PathOf());

        Assert.False(proof.PenMarksSeen);
        proof.RecordPenMarksSeen();

        Assert.True(new FileInkProof(PathOf()).PenMarksSeen);
    }

    [Fact]
    public void AFileSayingNoIsNotSeen()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf())!);
        File.WriteAllText(PathOf(), "{\"penMarksSeen\":false}");

        Assert.False(new FileInkProof(PathOf()).PenMarksSeen);
    }
}
