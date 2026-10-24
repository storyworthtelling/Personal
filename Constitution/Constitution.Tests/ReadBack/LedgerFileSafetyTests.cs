using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.ReadBack;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// The read-back against the real ledger file (review finding S1). A ledger the app can only partly read is the only
/// copy of the part it cannot: saving the readable part back is how that part is lost, so the file is left byte for byte as it was.
/// </summary>
public sealed class LedgerFileSafetyTests : IDisposable
{
    private const string WithAnUnknownKind =
        "{\"version\":1,\"records\":["
        + "{\"person\":\"Juniper\",\"day\":\"2026-10-30\",\"kind\":\"strength\",\"outcome\":\"done\"},"
        + "{\"person\":\"Juniper\",\"day\":\"2026-10-31\",\"kind\":\"yoga\",\"outcome\":\"done\"}]}";

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-ledger-");

    public void Dispose() => folder.Delete(recursive: true);

    private string Written(string text)
    {
        var path = Path.Combine(folder.FullName, "skip-ledger.json");
        File.WriteAllText(path, text);
        return path;
    }

    private static ReadBackService ServiceOver(Rig rig, string ledgerPath) =>
        new(rig.Expected, rig.Tablet, rig.Vision, rig.Results, new FileLedgerStore(ledgerPath), rig.Proof, rig.Notices, rig.Log, () => rig.Now);

    private static Rig Arranged()
    {
        var rig = new Rig(true, Workout());
        rig.Vision.Reply = document => Read(document, PenStrokes.Seen, (2, "done"));
        return rig;
    }

    [Fact]
    public async Task AnEntryOfAnUnknownKindIsNotDroppedByTheNextSave()
    {
        var rig = Arranged();
        var path = Written(WithAnUnknownKind);

        var report = await ServiceOver(rig, path).Run();

        var failed = Assert.IsType<DocumentReadBack.Failed>(Assert.Single(report.Documents));
        Assert.Equal(ReadBackStage.Persist, failed.Stage);
        Assert.IsType<LedgerUnreadableException>(failed.Failure);
        Assert.Equal(WithAnUnknownKind, File.ReadAllText(path));
        Assert.Empty(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task AnEntryWithAnUnknownOutcomeIsNotDroppedEither()
    {
        var rig = Arranged();
        var text = WithAnUnknownKind.Replace("\"kind\":\"yoga\",\"outcome\":\"done\"", "\"kind\":\"strength\",\"outcome\":\"postponed\"", StringComparison.Ordinal);
        var path = Written(text);

        var report = await ServiceOver(rig, path).Run();

        Assert.IsType<DocumentReadBack.Failed>(Assert.Single(report.Documents));
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public async Task AFileTheAppFullyReadsIsStillWritten()
    {
        var rig = Arranged();
        var path = Written("{\"version\":1,\"records\":[{\"person\":\"Juniper\",\"day\":\"2026-10-30\",\"kind\":\"strength\",\"outcome\":\"done\"}]}");

        var report = await ServiceOver(rig, path).Run();

        Assert.IsType<DocumentReadBack.Settled>(Assert.Single(report.Documents));
        Assert.Contains("2026-11-03", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
