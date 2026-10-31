using Pulp.Domain;
using Pulp.Domain.Household;
using Pulp.Infrastructure.Household;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Pulp.Tests.Infrastructure.Household;

/// <summary>
/// The household file in the owner's Drive (finding §2.7), encrypted under the vault passphrase both
/// adults hold. G14 is the prayer-archive rule: a file this phone cannot open is never written over —
/// a wrong passphrase on one phone must not encrypt its own small household over the real one.
/// </summary>
public sealed class DriveHouseholdStoreTests
{
    private const string Passphrase = "invented-household-passphrase";

    private static HouseholdRecord Viewer(string id, long wall, string writer) =>
        HouseholdRecord.Write(
            new RecordKey("viewer", id),
            1,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["child"] = "false" },
            new HlcTimestamp(wall, 0),
            writer);

    private static HouseholdDocument Of(params HouseholdRecord[] records) =>
        records.Aggregate(HouseholdDocument.Empty, (document, record) => document.With(record));

    private static string Sealed(HouseholdDocument document, string passphrase = Passphrase) =>
        VaultEnvelope.Write(HouseholdDocumentCodec.Serialize(document), passphrase);

    private static DriveHouseholdStore Store(FakeDrive drive, string passphrase = Passphrase, bool mayCreate = true) =>
        new(drive, PulpIdentity.HouseholdFileName, () => passphrase, () => mayCreate);

    private static HouseholdDocument Opened(FakeDrive drive) =>
        HouseholdDocumentCodec.Deserialize(VaultEnvelope.Read(drive.Content, Passphrase).Plaintext!);

    [Fact]
    public void TheFileNameIsTheFrozenOne() =>
        Assert.Equal(PulpIdentity.HouseholdFileName, Store(new FakeDrive()).FileName);

    [Fact]
    public async Task AnAbsentFileLoadsAsAnEmptyHousehold()
    {
        var loaded = await Store(new FakeDrive()).LoadAsync(CancellationToken.None);

        Assert.Empty(loaded.Records);
    }

    [Fact]
    public async Task WhatIsSavedIsLoadedAndDriveHoldsOnlyCiphertext()
    {
        var drive = new FakeDrive();
        var household = Of(Viewer("robin-example", 10, "install-a-example"));

        await Store(drive).SaveAsync(household, CancellationToken.None);

        Assert.DoesNotContain("robin-example", drive.Content, StringComparison.Ordinal);
        Assert.True((await Store(drive).LoadAsync(CancellationToken.None)).ContentEquals(household));
    }

    [Fact]
    public async Task AFileUnderAnotherPassphraseIsRefusedOnLoadNotReadAsEmpty()
    {
        var drive = new FakeDrive { Content = Sealed(Of(Viewer("robin-example", 10, "install-b-example")), "another-invented-passphrase") };

        var refused = await Assert.ThrowsAsync<HouseholdUnreadableException>(() => Store(drive).LoadAsync(CancellationToken.None));

        Assert.Equal(HouseholdUnreadableReason.WrongPassphrase, refused.Reason);
    }

    [Fact]
    public async Task ASaveOverAFileUnderAnotherPassphraseIsRefusedAndChangesNothing()
    {
        var original = Sealed(Of(Viewer("robin-example", 10, "install-b-example")), "another-invented-passphrase");
        var drive = new FakeDrive { Content = original };

        var refused = await Assert.ThrowsAsync<HouseholdUnreadableException>(
            () => Store(drive).SaveAsync(Of(Viewer("avery-example", 20, "install-a-example")), CancellationToken.None));

        Assert.Equal(HouseholdUnreadableReason.WrongPassphrase, refused.Reason);
        Assert.Equal(original, drive.Content);
        Assert.Equal(0, drive.Writes);
    }

    [Fact]
    public async Task ASaveOverADamagedFileIsRefused()
    {
        var drive = new FakeDrive { Content = "{\"Version\":1,\"Ciph" };

        var refused = await Assert.ThrowsAsync<HouseholdUnreadableException>(
            () => Store(drive).SaveAsync(Of(Viewer("avery-example", 20, "install-a-example")), CancellationToken.None));

        Assert.Equal(HouseholdUnreadableReason.Damaged, refused.Reason);
        Assert.Equal(0, drive.Writes);
    }

    [Fact]
    public async Task ASaveOverAFileThatOpensButIsNotAHouseholdIsRefused()
    {
        var drive = new FakeDrive { Content = VaultEnvelope.Write("{\"records\":[]}", Passphrase) };

        var refused = await Assert.ThrowsAsync<HouseholdUnreadableException>(
            () => Store(drive).SaveAsync(Of(Viewer("avery-example", 20, "install-a-example")), CancellationToken.None));

        Assert.Equal(HouseholdUnreadableReason.Damaged, refused.Reason);
        Assert.Equal(0, drive.Writes);
    }

    [Fact]
    public async Task ASaveOverAFileFromANewerPulpIsRefused()
    {
        var drive = new FakeDrive { Content = VaultEnvelope.Write("{\"format\":9,\"records\":[]}", Passphrase) };

        var refused = await Assert.ThrowsAsync<HouseholdUnreadableException>(
            () => Store(drive).SaveAsync(Of(Viewer("avery-example", 20, "install-a-example")), CancellationToken.None));

        Assert.Equal(HouseholdUnreadableReason.NewerFormat, refused.Reason);
        Assert.Equal(0, drive.Writes);
    }

    [Fact]
    public async Task WithoutAPassphraseNothingIsWritten()
    {
        var drive = new FakeDrive();

        var refused = await Assert.ThrowsAsync<HouseholdUnreadableException>(
            () => Store(drive, passphrase: string.Empty).SaveAsync(Of(Viewer("avery-example", 20, "install-a-example")), CancellationToken.None));

        Assert.Equal(HouseholdUnreadableReason.NoPassphrase, refused.Reason);
        Assert.Equal(0, drive.Writes);
    }

    [Fact]
    public async Task ASaveAfterAConcurrentRemoteWriteKeepsBothWritersRecords()
    {
        // Both phones read the same file. This phone writes its merge; between that write and its
        // re-read, the other phone's write lands — the merge of what *it* read, so ours is gone.
        var start = Of(Viewer("robin-example", 10, "install-a-example"));
        var theirs = start.With(Viewer("avery-example", 20, "install-b-example"));
        var ours = start.With(Viewer("jules-example", 30, "install-a-example"));
        var drive = new FakeDrive { Content = Sealed(start) };
        drive.AfterWrite = write => write == 1 ? Sealed(theirs) : null;

        var result = await Store(drive).PublishAsync(ours, CancellationToken.None);

        var onDrive = Opened(drive);
        Assert.NotNull(onDrive.Live(new RecordKey("viewer", "jules-example")));
        Assert.NotNull(onDrive.Live(new RecordKey("viewer", "avery-example")));
        Assert.NotNull(onDrive.Live(new RecordKey("viewer", "robin-example")));
        Assert.True(result.Held.ContentEquals(onDrive));
        Assert.Equal(HouseholdPublishOutcome.Written, result.Outcome);
        Assert.Equal(2, drive.Writes);
    }

    [Fact]
    public async Task AWriteThatKeepsLosingIsKeptOnThePhoneForTheNextPass()
    {
        var theirs = Of(Viewer("avery-example", 20, "install-b-example"));
        var ours = Of(Viewer("jules-example", 30, "install-a-example"));
        var drive = new FakeDrive();
        drive.AfterWrite = _ => Sealed(theirs);

        var result = await Store(drive).PublishAsync(ours, CancellationToken.None);

        Assert.Equal(HouseholdPublishOutcome.LeftForNextPass, result.Outcome);
        Assert.Equal(HouseholdPublish.MaxWrites, drive.Writes);
        Assert.NotNull(result.Held.Live(new RecordKey("viewer", "jules-example")));
        Assert.NotNull(result.Held.Live(new RecordKey("viewer", "avery-example")));
    }

    [Fact]
    public async Task APassThatChangesNothingWritesNothing()
    {
        var household = Of(Viewer("robin-example", 10, "install-b-example"));
        var drive = new FakeDrive { Content = Sealed(household) };

        var result = await Store(drive).PublishAsync(household, CancellationToken.None);

        Assert.Equal(HouseholdPublishOutcome.AlreadyCurrent, result.Outcome);
        Assert.Equal(0, drive.Writes);
    }

    [Fact]
    public async Task APassBringsTheOtherPhonesRecordsHome()
    {
        var drive = new FakeDrive { Content = Sealed(Of(Viewer("avery-example", 20, "install-b-example"))) };

        var result = await Store(drive).PublishAsync(HouseholdDocument.Empty, CancellationToken.None);

        Assert.NotNull(result.Held.Live(new RecordKey("viewer", "avery-example")));
        Assert.Equal(0, drive.Writes);
    }

    [Fact]
    public async Task APhoneTheFileIsSharedWithNeverCreatesItsOwnCopy()
    {
        // The file lives in the owner's Drive. Before the owner shares it, the other adult's phone
        // finds nothing — and a file it made would be a second household nobody merges with.
        var drive = new FakeDrive();
        var ours = Of(Viewer("jules-example", 30, "install-b-example"));

        var result = await Store(drive, mayCreate: false).PublishAsync(ours, CancellationToken.None);

        Assert.Equal(HouseholdPublishOutcome.NotYetShared, result.Outcome);
        Assert.Equal(0, drive.Writes);
        Assert.True(result.Held.ContentEquals(ours));
    }

    [Fact]
    public async Task ASaveThatCouldNotLandAnywhereSaysSo()
    {
        var drive = new FakeDrive();

        await Assert.ThrowsAsync<HouseholdNotSharedException>(
            () => Store(drive, mayCreate: false).SaveAsync(Of(Viewer("jules-example", 30, "install-b-example")), CancellationToken.None));
    }

    [Fact]
    public async Task AnUnreachableDriveIsNotMistakenForAnUnreadableFile()
    {
        var drive = new FakeDrive { Unreachable = new HttpRequestException("The invented network is down.") };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Store(drive).PublishAsync(Of(Viewer("jules-example", 30, "install-a-example")), CancellationToken.None));
    }

    /// <summary>A Drive file in memory that can be made to lose a race or to be out of reach.</summary>
    private sealed class FakeDrive : IDriveTextFile
    {
        public string? Content { get; set; }

        public int Writes { get; private set; }

        /// <summary>Given the write's number, what another writer leaves in the file after it (null: nothing).</summary>
        public Func<int, string?>? AfterWrite { get; set; }

        public Exception? Unreachable { get; set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken) =>
            Unreachable is { } failure ? Task.FromException<string?>(failure) : Task.FromResult(Content);

        public Task WriteAsync(string content, CancellationToken cancellationToken)
        {
            if (Unreachable is { } failure)
            {
                return Task.FromException(failure);
            }

            Writes++;
            Content = AfterWrite?.Invoke(Writes) ?? content;
            return Task.CompletedTask;
        }
    }
}
