using Foreword.Domain.Prayer;
using Foreword.Infrastructure.Prayer;
using Storyworthtelling.Voice;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// The private archive: encrypted with the owner's vault passphrase, in one Drive file.
///
/// <para>It is the <b>only</b> copy of what they wrote — the journal page is trashed off the tablet
/// once it has been read — so the rule that matters more than any feature here is that a save can
/// never destroy an archive this code could not read.</para>
/// </summary>
public class DrivePrayerArchiveTests
{
    private const string Passphrase = "the one thing not in the vault";

    private sealed class InMemoryDrive : IDriveTextFile
    {
        public string? Content { get; set; }

        public int Writes { get; private set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Content);

        public Task WriteAsync(string content, CancellationToken cancellationToken)
        {
            Content = content;
            Writes++;
            return Task.CompletedTask;
        }
    }

    private sealed class Reader : IPrayerReader
    {
    }

    private static string Read(PrayerText text) => text.ReadBy(new Reader());

    private static PrayerArchive Sample() => new()
    {
        Requests =
        [
            new PrayerRequest
            {
                Id = "dad-scan",
                Text = PrayerText.Of("Dad — the scan on the 14th"),
                Held = new DateOnly(2026, 8, 28),
            },
        ],
        Journal =
        [
            new JournalEntry
            {
                Date = new DateOnly(2026, 9, 5),
                Text = PrayerText.Of("Asked for patience with the mornings."),
            },
        ],
    };

    [Fact]
    public async Task WhatItSavesItLoadsBack()
    {
        var drive = new InMemoryDrive();
        var archive = new DrivePrayerArchive(drive);

        await archive.SaveAsync(Sample(), Passphrase, CancellationToken.None);
        var loaded = await archive.LoadAsync(Passphrase, CancellationToken.None);

        Assert.Equal(PrayerArchiveStatus.Loaded, loaded.Status);
        Assert.Equal("Dad — the scan on the 14th", Read(loaded.Archive.Requests[0].Text));
        Assert.Equal("Asked for patience with the mornings.", Read(loaded.Archive.Journal[0].Text));
    }

    /// <summary>
    /// The whole point of the owner's choice. Anyone with the Drive — a shared laptop, a future
    /// connector, a support session — sees ciphertext and nothing else.
    /// </summary>
    [Fact]
    public async Task WhatSitsOnDriveIsCiphertext()
    {
        var drive = new InMemoryDrive();

        await new DrivePrayerArchive(drive).SaveAsync(Sample(), Passphrase, CancellationToken.None);

        Assert.NotNull(drive.Content);
        Assert.DoesNotContain("scan", drive.Content!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("patience", drive.Content!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ciphertext", drive.Content!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AWrongPassphraseSaysSoRatherThanReturningNothing()
    {
        var drive = new InMemoryDrive();
        var archive = new DrivePrayerArchive(drive);

        await archive.SaveAsync(Sample(), Passphrase, CancellationToken.None);

        var loaded = await archive.LoadAsync("not the passphrase", CancellationToken.None);

        Assert.Equal(PrayerArchiveStatus.WrongPassphrase, loaded.Status);
        Assert.Empty(loaded.Archive.Requests);
    }

    /// <summary>
    /// The one rule that outranks everything else here. A wrong passphrase on a save would
    /// otherwise re-encrypt an empty archive over the top of a real one, and there is no second
    /// copy to recover from — so the save is <b>refused</b>.
    /// </summary>
    [Fact]
    public async Task ASaveUnderTheWrongPassphraseIsRefusedAndChangesNothing()
    {
        var drive = new InMemoryDrive();
        var archive = new DrivePrayerArchive(drive);

        await archive.SaveAsync(Sample(), Passphrase, CancellationToken.None);
        var onDrive = drive.Content;
        var writes = drive.Writes;

        await Assert.ThrowsAsync<PrayerArchiveLockedException>(() =>
            archive.SaveAsync(new PrayerArchive(), "not the passphrase", CancellationToken.None));

        Assert.Equal(onDrive, drive.Content);
        Assert.Equal(writes, drive.Writes);
    }

    /// <summary>
    /// And a file that will not decrypt at all is treated the same way, because the difference
    /// between "corrupt" and "wrong passphrase" does not change what is at stake: something is
    /// there and this code cannot read it.
    /// </summary>
    [Fact]
    public async Task ASaveOverAnUnreadableFileIsRefused()
    {
        var drive = new InMemoryDrive { Content = "{ not an envelope" };
        var archive = new DrivePrayerArchive(drive);

        await Assert.ThrowsAsync<PrayerArchiveLockedException>(() =>
            archive.SaveAsync(Sample(), Passphrase, CancellationToken.None));

        Assert.Equal("{ not an envelope", drive.Content);
    }

    /// <summary>
    /// A first morning has no file, which is not a failure — it is the state every install starts
    /// in, and a save has to be allowed or nothing is ever written.
    /// </summary>
    [Fact]
    public async Task AnAbsentFileLoadsEmptyAndCanBeWrittenTo()
    {
        var drive = new InMemoryDrive();
        var archive = new DrivePrayerArchive(drive);

        var loaded = await archive.LoadAsync(Passphrase, CancellationToken.None);

        Assert.Equal(PrayerArchiveStatus.Empty, loaded.Status);
        Assert.Empty(loaded.Archive.Journal);

        await archive.SaveAsync(Sample(), Passphrase, CancellationToken.None);
        Assert.NotNull(drive.Content);
    }

    /// <summary>
    /// No passphrase means no archive — and, crucially, no save either. An app whose passphrase has
    /// been wiped by a factory reset must not helpfully encrypt the journal under an empty string.
    /// </summary>
    [Fact]
    public async Task WithoutAPassphraseNothingIsReadAndNothingIsWritten()
    {
        var drive = new InMemoryDrive();
        var archive = new DrivePrayerArchive(drive);

        var loaded = await archive.LoadAsync("", CancellationToken.None);
        Assert.Equal(PrayerArchiveStatus.Empty, loaded.Status);

        await Assert.ThrowsAsync<PrayerArchiveLockedException>(() =>
            archive.SaveAsync(Sample(), "", CancellationToken.None));

        Assert.Null(drive.Content);
    }

    [Fact]
    public void TheArchiveIsADeclaredReader()
    {
        Assert.IsAssignableFrom<IPrayerReader>(new DrivePrayerArchive(new InMemoryDrive()));
    }

    /// <summary>
    /// The file name is the address. It is found by name across installs, so a rename orphans an
    /// archive that is still sitting there.
    /// </summary>
    [Fact]
    public void TheFileNameIsFrozen()
    {
        Assert.Equal("storyworthtelling-prayer-archive.json", DrivePrayerArchive.FileName);
    }
}
