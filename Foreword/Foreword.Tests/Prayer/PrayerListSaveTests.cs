using Foreword.Domain.Interfaces;
using Foreword.Domain.Prayer;
using Foreword.Infrastructure.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// Audit 11 §25: every failed save of the rotations returned <c>false</c>, so the screen could not tell no network from
/// an archive that refused to be written over. Each now comes back as its own outcome with its own sentence.
/// </summary>
public class PrayerListSaveTests
{
    private sealed class Archive(PrayerArchiveStatus status, Exception? onLoad = null, Exception? onSave = null) : IPrayerArchive
    {
        public PrayerArchive? Saved { get; private set; }

        public Task<PrayerArchiveRead> LoadAsync(string passphrase, CancellationToken cancellationToken) =>
            onLoad is null ? Task.FromResult(PrayerArchiveRead.Nothing(status)) : Task.FromException<PrayerArchiveRead>(onLoad);

        public Task SaveAsync(PrayerArchive archive, string passphrase, CancellationToken cancellationToken)
        {
            if (onSave is not null)
            {
                return Task.FromException(onSave);
            }
            Saved = archive;
            return Task.CompletedTask;
        }
    }

    private static Task<PrayerListSave> Save(Archive archive) => PrayerListSave.Run(archive, "example-passphrase", []);

    [Fact]
    public async Task AnArchiveThatOpensIsSaved()
    {
        var archive = new Archive(PrayerArchiveStatus.Loaded);

        var result = await Save(archive);

        Assert.Equal(PrayerSaveOutcome.Saved, result.Outcome);
        Assert.NotNull(archive.Saved);
    }

    [Theory]
    [InlineData(PrayerArchiveStatus.WrongPassphrase, PrayerSaveOutcome.WrongPassphrase)]
    [InlineData(PrayerArchiveStatus.Corrupt, PrayerSaveOutcome.Unreadable)]
    public async Task AnArchiveThatWillNotOpenIsNotWritten(PrayerArchiveStatus status, PrayerSaveOutcome expected)
    {
        var archive = new Archive(status);

        Assert.Equal(expected, (await Save(archive)).Outcome);
        Assert.Null(archive.Saved);
    }

    [Fact]
    public async Task NoNetworkIsSaidAsNoNetwork()
    {
        var offline = new HttpRequestException("Connection failure",
            new IOException("Unable to resolve host \"www.googleapis.com\": No address associated with hostname"));

        Assert.Equal(PrayerSaveOutcome.Offline, (await Save(new Archive(PrayerArchiveStatus.Loaded, onLoad: offline))).Outcome);
        Assert.Equal(PrayerSaveOutcome.Offline, (await Save(new Archive(PrayerArchiveStatus.Loaded, onSave: offline))).Outcome);
    }

    [Fact]
    public async Task AnArchiveThatRefusesToBeWrittenOverIsSaidAsSuch() =>
        Assert.Equal(PrayerSaveOutcome.Refused,
            (await Save(new Archive(PrayerArchiveStatus.Loaded, onSave: new PrayerArchiveLockedException("refused")))).Outcome);

    [Fact]
    public async Task AnythingElseIsAFailedSaveWithItsCauseInPlainWords()
    {
        var result = await Save(new Archive(PrayerArchiveStatus.Loaded, onSave: new IOException("write failed: ENOSPC (No space left on device)")));

        Assert.Equal(PrayerSaveOutcome.Failed, result.Outcome);
        Assert.Contains("out of storage space", result.Sentence());
    }

    [Fact]
    public void EveryOutcomeButSavedHasItsOwnSentence()
    {
        var sentences = Enum.GetValues<PrayerSaveOutcome>()
            .Where(outcome => outcome != PrayerSaveOutcome.Saved)
            .Select(outcome => new PrayerListSave(outcome, PrayerArchiveStatus.Loaded, string.Empty).Sentence())
            .ToList();

        Assert.Equal(sentences.Count, sentences.Distinct().Count());
        Assert.All(sentences, sentence => Assert.Contains("Nothing was changed", sentence));
    }
}
