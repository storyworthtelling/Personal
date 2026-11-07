using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Tests.Secrets;

// Duplication audit §5, contracts M6, error-handling §3. Six encrypted Drive files each had their own rule for
// when a write may replace what is there, and they disagreed: Index's snapshot overwrote a corrupt file, the
// vault overwrote whatever a failed read made look empty, and nothing noticed a write from another phone in
// between. SealedDriveFile is the one rule (G14): write only over a file that opened, or none at all when this
// install may create it; not over a newer format; not over a file that changed since it was read; and read
// the result back, putting the previous file back if it does not open.
public class SealedDriveFileTests
{
    private const string Key = "invented sample passphrase";

    [Fact]
    public async Task AFileThatIsNotThereReadsAsAbsentAndMayBeCreated()
    {
        var drive = new MemoryDrive();
        var file = new SealedDriveFile(drive);

        var read = await file.ReadAsync(Key, default);
        var written = await file.WriteAsync("{\"a\":1}", Key, read, SealedWriteOptions.Default, default);

        Assert.Equal(SealedReadStatus.Absent, read.Status);
        Assert.Equal(SealedWriteOutcome.Written, written.Outcome);
        Assert.Equal("{\"a\":1}", (await file.ReadAsync(Key, default)).Plaintext);
    }

    [Fact]
    public async Task AnInstallThatMayNotCreateIsRefusedWhenThereIsNoFile()
    {
        var drive = new MemoryDrive();
        var file = new SealedDriveFile(drive);

        var written = await file.WriteAsync("{}", Key, await file.ReadAsync(Key, default), new SealedWriteOptions(MayCreate: false), default);

        Assert.Equal(SealedWriteOutcome.Refused, written.Outcome);
        Assert.Null(drive.Content);
    }

    [Theory]
    [InlineData("not an envelope")]
    [InlineData("")]
    public async Task ACorruptFileIsNeverWrittenOver(string content)
    {
        var drive = new MemoryDrive { Content = content };
        var file = new SealedDriveFile(drive);

        var read = await file.ReadAsync(Key, default);
        var written = await file.WriteAsync("{}", Key, read, SealedWriteOptions.Default, default);

        Assert.Equal(SealedReadStatus.Corrupt, read.Status);
        Assert.Equal(SealedWriteOutcome.Refused, written.Outcome);
        Assert.Equal(content, drive.Content);
    }

    [Fact]
    public async Task AWrongPassphraseIsNeverWrittenOver()
    {
        var drive = new MemoryDrive { Content = VaultEnvelope.Write("{}", "the other passphrase") };
        var before = drive.Content;
        var file = new SealedDriveFile(drive);

        var read = await file.ReadAsync(Key, default);
        var written = await file.WriteAsync("{}", Key, read, SealedWriteOptions.Default, default);

        Assert.Equal(SealedReadStatus.WrongPassphrase, read.Status);
        Assert.Equal(SealedWriteOutcome.Refused, written.Outcome);
        Assert.Equal(before, drive.Content);
    }

    [Fact]
    public async Task ANewerFormatIsNeverWrittenOver()
    {
        var drive = new MemoryDrive { Content = VaultEnvelope.Write("{\"schema\":9}", Key) };
        var before = drive.Content;
        var file = new SealedDriveFile(drive, plaintext => plaintext.Contains("\"schema\":9", StringComparison.Ordinal));

        var read = await file.ReadAsync(Key, default);
        var written = await file.WriteAsync("{\"schema\":1}", Key, read, SealedWriteOptions.Default, default);

        Assert.Equal(SealedReadStatus.NewerFormat, read.Status);
        Assert.Equal(SealedWriteOutcome.Refused, written.Outcome);
        Assert.Equal(before, drive.Content);
    }

    [Fact]
    public async Task AFailedReadIsUnreachableAndNeverWrittenOver()
    {
        var drive = new MemoryDrive { Content = VaultEnvelope.Write("{}", Key), FailReads = true };
        var before = drive.Content;
        var file = new SealedDriveFile(drive);

        var read = await file.ReadAsync(Key, default);
        var written = await file.WriteAsync("{}", Key, read, SealedWriteOptions.Default, default);

        Assert.Equal(SealedReadStatus.Unreachable, read.Status);
        Assert.Equal(SealedWriteOutcome.Refused, written.Outcome);
        Assert.Equal(before, drive.Content);
    }

    [Fact]
    public async Task AFileThatChangedSinceItWasReadIsAConflictNotAnOverwrite()
    {
        var drive = new MemoryDrive { Content = VaultEnvelope.Write("{\"from\":\"first\"}", Key) };
        var file = new SealedDriveFile(drive);
        var read = await file.ReadAsync(Key, default);

        // Another phone writes in between.
        drive.Content = VaultEnvelope.Write("{\"from\":\"other phone\"}", Key);
        var other = drive.Content;

        var written = await file.WriteAsync("{\"from\":\"this phone\"}", Key, read, SealedWriteOptions.Default, default);

        Assert.Equal(SealedWriteOutcome.Conflict, written.Outcome);
        Assert.Equal(other, drive.Content);
    }

    [Fact]
    public async Task AFileCreatedByAnotherPhoneSinceTheReadIsAConflict()
    {
        var drive = new MemoryDrive();
        var file = new SealedDriveFile(drive);
        var read = await file.ReadAsync(Key, default);
        drive.Content = VaultEnvelope.Write("{\"from\":\"other phone\"}", Key);

        var written = await file.WriteAsync("{}", Key, read, SealedWriteOptions.Default, default);

        Assert.Equal(SealedWriteOutcome.Conflict, written.Outcome);
    }

    [Fact]
    public async Task AWriteThatDoesNotReadBackPutsThePreviousFileBack()
    {
        var drive = new MemoryDrive { Content = VaultEnvelope.Write("{\"kept\":true}", Key) };
        var previous = drive.Content;
        var file = new SealedDriveFile(drive);
        var read = await file.ReadAsync(Key, default);
        drive.DamageNextWrite = true;

        var written = await file.WriteAsync("{\"kept\":false}", Key, read, SealedWriteOptions.Default, default);

        Assert.Equal(SealedWriteOutcome.Unconfirmed, written.Outcome);
        Assert.Equal(previous, drive.Content);
    }

    [Fact]
    public async Task AFirstWriteThatDoesNotReadBackIsTrashedNotLeftDamaged()
    {
        var drive = new MemoryDrive { DamageNextWrite = true };
        var file = new SealedDriveFile(drive);

        var written = await file.WriteAsync("{}", Key, await file.ReadAsync(Key, default), SealedWriteOptions.Default, default);

        Assert.Equal(SealedWriteOutcome.Unconfirmed, written.Outcome);
        Assert.Null(drive.Content);
        Assert.Equal(SealedReadStatus.Absent, (await file.ReadAsync(Key, default)).Status);
    }

    [Fact]
    public async Task AWriteThatCannotBeReadBackIsReportedNotTrusted()
    {
        var drive = new MemoryDrive();
        var file = new SealedDriveFile(drive);
        var read = await file.ReadAsync(Key, default);
        drive.FailReadsAfterWrite = true;

        var written = await file.WriteAsync("{}", Key, read, SealedWriteOptions.Default, default);

        Assert.Equal(SealedWriteOutcome.Unverified, written.Outcome);
    }

    [Fact]
    public async Task AFailedWriteSaysSoAndLeavesTheFile()
    {
        var drive = new MemoryDrive { Content = VaultEnvelope.Write("{}", Key), FailWrites = true };
        var before = drive.Content;
        var file = new SealedDriveFile(drive);

        var written = await file.WriteAsync("{\"x\":1}", Key, await file.ReadAsync(Key, default), SealedWriteOptions.Default, default);

        Assert.Equal(SealedWriteOutcome.Failed, written.Outcome);
        Assert.Equal(before, drive.Content);
    }

    [Fact]
    public async Task NoOutcomeCarriesThePlaintextOrTheKey()
    {
        var drive = new MemoryDrive { DamageNextWrite = true };
        var file = new SealedDriveFile(drive);

        var written = await file.WriteAsync("{\"secret\":\"sk-invented\"}", Key, await file.ReadAsync(Key, default), SealedWriteOptions.Default, default);

        Assert.DoesNotContain("sk-invented", written.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, written.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>A Drive file with a revision that moves on every write, and the failures a phone meets.</summary>
    internal sealed class MemoryDrive : IDriveTextFile, IDriveFileRevision, IDriveFileTrash
    {
        private string? content;
        private int revision;

        public string? Content
        {
            get => content;
            set
            {
                content = value;
                revision++;
            }
        }

        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }
        public bool FailReadsAfterWrite { get; set; }
        public bool DamageNextWrite { get; set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken) =>
            FailReads ? throw new IOException("Drive could not read the file.") : Task.FromResult(content);

        public Task WriteAsync(string value, CancellationToken cancellationToken)
        {
            if (FailWrites)
            {
                throw new IOException("Drive refused the upload.");
            }
            Content = DamageNextWrite ? value[..(value.Length / 2)] : value;
            DamageNextWrite = false;
            if (FailReadsAfterWrite)
            {
                FailReads = true;
            }
            return Task.CompletedTask;
        }

        public Task<string?> RevisionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(content is null ? null : revision.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public Task TrashAsync(CancellationToken cancellationToken)
        {
            content = null;
            revision++;
            return Task.CompletedTask;
        }
    }
}
