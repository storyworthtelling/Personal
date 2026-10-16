using Pulp.Domain.Core;
using Pulp.Domain.Services;
using Pulp.Domain.Usage;
using Pulp.Infrastructure.Usage;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Pulp.Tests.Usage;

/// <summary>The usage file in Drive (D320): encrypted under the household passphrase, and never written over a file this phone cannot open (G14).</summary>
public sealed class DriveIndexUsageStoreTests
{
    private const string Passphrase = "invented-usage-passphrase";

    private static readonly ServiceCatalogue Catalogue = ServiceCatalogue.Public;

    private sealed class FakeDrive : IDriveTextFile
    {
        public string? Content { get; set; }

        public int Writes { get; private set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Content);

        public Task WriteAsync(string content, CancellationToken cancellationToken)
        {
            Writes++;
            Content = content;
            return Task.CompletedTask;
        }
    }

    private static IndexUsagePayload Payload(string service, int count) =>
        new(new ServiceId(service), new UsageMonth(2026, 9), count, new DateOnly(2026, 9, 5));

    private static DriveIndexUsageStore Store(FakeDrive drive, string passphrase = Passphrase, bool mayCreate = true) =>
        new(drive, () => passphrase, Catalogue, () => mayCreate);

    private static IReadOnlyList<IndexUsageEntry> Opened(FakeDrive drive)
    {
        Assert.True(IndexUsageFile.TryRead(VaultEnvelope.Read(drive.Content, Passphrase).Plaintext!, out var entries));
        return entries!;
    }

    [Fact]
    public async Task AnAbsentFileIsCreatedAndDriveHoldsOnlyCiphertext()
    {
        var drive = new FakeDrive();

        await Store(drive).PublishAsync([Payload("netflix", 4)], CancellationToken.None);

        Assert.DoesNotContain("netflix", drive.Content, StringComparison.Ordinal);
        Assert.Equal(4, Opened(drive).Single().Count);
    }

    [Fact]
    public async Task AnAccountThatMayNotCreateTheFileLeavesItAbsentRatherThanMakeOneNobodyElseSees()
    {
        var drive = new FakeDrive();

        await Store(drive, mayCreate: false).PublishAsync([Payload("netflix", 4)], CancellationToken.None);

        Assert.Null(drive.Content);
    }

    [Fact]
    public async Task AnAccountThatMayNotCreateTheFileStillUpdatesOneThatIsShared()
    {
        var drive = new FakeDrive();
        await Store(drive).PublishAsync([Payload("netflix", 4)], CancellationToken.None);

        await Store(drive, mayCreate: false).PublishAsync([Payload("hulu", 2)], CancellationToken.None);

        Assert.Equal(2, Opened(drive).Count);
    }

    [Fact]
    public async Task APublishMergesIntoWhatIsThereAndSkipsTheWriteWhenNothingChanged()
    {
        var drive = new FakeDrive();
        var store = Store(drive);
        await store.PublishAsync([Payload("netflix", 4)], CancellationToken.None);
        await store.PublishAsync([Payload("hulu", 2)], CancellationToken.None);
        var writes = drive.Writes;

        await store.PublishAsync([Payload("hulu", 2)], CancellationToken.None);

        Assert.Equal(writes, drive.Writes);
        Assert.Equal(["hulu", "netflix"], Opened(drive).Select(entry => entry.Service.Value).Order().ToList());
    }

    [Fact]
    public async Task AWrongPassphraseLeavesTheFileExactlyAsItIs()
    {
        var drive = new FakeDrive();
        await Store(drive).PublishAsync([Payload("netflix", 4)], CancellationToken.None);
        var before = drive.Content;
        var writes = drive.Writes;

        await Assert.ThrowsAsync<IndexUsageUnreadableException>(() =>
            Store(drive, "another-invented-passphrase").PublishAsync([Payload("hulu", 1)], CancellationToken.None));

        Assert.Equal(before, drive.Content);
        Assert.Equal(writes, drive.Writes);
    }

    [Fact]
    public async Task AGarbledFileIsNeverWrittenOver()
    {
        var drive = new FakeDrive { Content = "this is not an envelope" };

        await Assert.ThrowsAsync<IndexUsageUnreadableException>(() =>
            Store(drive).PublishAsync([Payload("netflix", 4)], CancellationToken.None));

        Assert.Equal("this is not an envelope", drive.Content);
    }

    [Fact]
    public async Task AnOpenablePayloadThatIsNotAUsageFileIsNeverWrittenOver()
    {
        var drive = new FakeDrive { Content = VaultEnvelope.Write("{\"version\":9,\"entries\":[]}", Passphrase) };
        var before = drive.Content;

        await Assert.ThrowsAsync<IndexUsageUnreadableException>(() =>
            Store(drive).PublishAsync([Payload("netflix", 4)], CancellationToken.None));

        Assert.Equal(before, drive.Content);
    }

    [Fact]
    public async Task NoPassphraseMeansNothingIsReadOrWritten()
    {
        var drive = new FakeDrive();

        await Assert.ThrowsAsync<IndexUsageUnreadableException>(() =>
            Store(drive, string.Empty).PublishAsync([Payload("netflix", 4)], CancellationToken.None));

        Assert.Equal(0, drive.Writes);
    }
}
