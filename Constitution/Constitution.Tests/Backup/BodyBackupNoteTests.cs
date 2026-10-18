using System.Text.RegularExpressions;
using Constitution.Infrastructure.Backup;

namespace Constitution.Tests.Backup;

/// <summary>
/// The one inline sentence the Settings screen can show about the Body backup. Pure wording over typed
/// results: every way a restore, a backup or a run can end has a sentence, none carries a reading, a
/// number, a passphrase or a failure's message, and an outcome this wording does not know is refused
/// rather than voiced as something else.
/// </summary>
public class BodyBackupNoteTests
{
    public static IEnumerable<object[]> RestoreOutcomes() =>
        Enum.GetValues<BodyRestoreOutcome>().Select(outcome => new object[] { outcome });

    public static IEnumerable<object[]> BackupOutcomes() =>
        Enum.GetValues<BodyBackupOutcome>().Select(outcome => new object[] { outcome });

    public static IEnumerable<object[]> RunStatuses() =>
        Enum.GetValues<BodyBackupRunStatus>().Select(status => new object[] { status });

    private static void AssertASentence(string note)
    {
        Assert.False(string.IsNullOrWhiteSpace(note));
        Assert.EndsWith(".", note, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", note, StringComparison.Ordinal);
        Assert.False(Regex.IsMatch(note, @"\d"), "A Body note must carry no number: " + note);
        Assert.DoesNotContain("!", note, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RestoreOutcomes))]
    public void EveryRestoreOutcomeHasASentence(BodyRestoreOutcome outcome)
    {
        AssertASentence(BodyBackupNote.For(new BodyRestoreResult(outcome)));
    }

    [Theory]
    [MemberData(nameof(BackupOutcomes))]
    public void EveryBackupOutcomeHasASentence(BodyBackupOutcome outcome)
    {
        AssertASentence(BodyBackupNote.For(new BodyBackupResult(outcome)));
    }

    [Theory]
    [MemberData(nameof(RunStatuses))]
    public void EveryRunStatusHasASentence(BodyBackupRunStatus status)
    {
        var restore = new BodyRestoreResult(BodyRestoreOutcome.Restored);
        var backup = new BodyBackupResult(BodyBackupOutcome.BackedUp);
        var result = status switch
        {
            BodyBackupRunStatus.RestoreRan => new BodyBackupRunResult(status, Restore: restore),
            BodyBackupRunStatus.BackupRan => new BodyBackupRunResult(status, Backup: backup),
            _ => new BodyBackupRunResult(status),
        };

        AssertASentence(BodyBackupNote.For(result));
    }

    [Fact]
    public void NoTwoRestoreOutcomesShareASentence()
    {
        var notes = Enum.GetValues<BodyRestoreOutcome>().Select(outcome => BodyBackupNote.For(new BodyRestoreResult(outcome))).ToList();

        Assert.Equal(notes.Count, notes.Distinct().Count());
    }

    [Fact]
    public void NoTwoBackupOutcomesShareASentence()
    {
        var notes = Enum.GetValues<BodyBackupOutcome>().Select(outcome => BodyBackupNote.For(new BodyBackupResult(outcome))).ToList();

        Assert.Equal(notes.Count, notes.Distinct().Count());
    }

    [Fact]
    public void ARestoredHistoryIsSaidPlainly()
    {
        Assert.Equal(
            "Body history was brought back from the backup.",
            BodyBackupNote.For(new BodyRestoreResult(BodyRestoreOutcome.Restored)));
    }

    [Fact]
    public void AWrongPassphraseSaysTheBackupWasLeftAlone()
    {
        var note = BodyBackupNote.For(new BodyRestoreResult(BodyRestoreOutcome.WrongPassphrase));

        Assert.Contains("passphrase", note, StringComparison.Ordinal);
        Assert.Contains("left", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedBackupSaysNothingWasWrittenOverIt()
    {
        var note = BodyBackupNote.For(new BodyBackupResult(BodyBackupOutcome.RefusedUnreadable));

        Assert.Contains("left alone", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewerFormatAsksForAnUpdate()
    {
        var note = BodyBackupNote.For(new BodyBackupResult(BodyBackupOutcome.RefusedNewerFormat));

        Assert.Contains("newer version", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureNamesItsTypeAndNeverItsMessage()
    {
        var note = BodyBackupNote.For(new BodyBackupResult(
            BodyBackupOutcome.Failed,
            "HttpRequestException: the passphrase example-body-passphrase-one was rejected at 123.45"));

        Assert.Contains("HttpRequestException", note, StringComparison.Ordinal);
        Assert.DoesNotContain("example-body-passphrase-one", note, StringComparison.Ordinal);
        Assert.DoesNotContain("123.45", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ADetailThatIsNotATypeNameIsDropped()
    {
        var note = BodyBackupNote.For(new BodyBackupResult(
            BodyBackupOutcome.Failed,
            "The file written could not be read back; the previous file was put back."));

        Assert.DoesNotContain("could not be read back", note, StringComparison.Ordinal);
        Assert.DoesNotContain("(", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ACorruptRestoreNeverRepeatsWhatIsWrongWithTheFile()
    {
        var note = BodyBackupNote.For(new BodyRestoreResult(BodyRestoreOutcome.Corrupt, "the version field was 99"));

        Assert.DoesNotContain("99", note, StringComparison.Ordinal);
        Assert.DoesNotContain("version field", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunFailureNamesItsType()
    {
        var note = BodyBackupNote.For(new BodyBackupRunResult(BodyBackupRunStatus.Failed, Detail: "InvalidOperationException"));

        Assert.Contains("InvalidOperationException", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunThatRanSpeaksForWhatItRan()
    {
        var restore = new BodyRestoreResult(BodyRestoreOutcome.NoBackup);
        var backup = new BodyBackupResult(BodyBackupOutcome.Unchanged);

        Assert.Equal(BodyBackupNote.For(restore), BodyBackupNote.For(new BodyBackupRunResult(BodyBackupRunStatus.RestoreRan, Restore: restore)));
        Assert.Equal(BodyBackupNote.For(backup), BodyBackupNote.For(new BodyBackupRunResult(BodyBackupRunStatus.BackupRan, Backup: backup)));
    }

    [Theory]
    [InlineData(BodyBackupRunStatus.RestoreRan)]
    [InlineData(BodyBackupRunStatus.BackupRan)]
    public void ARunThatRanWithNoResultInItIsRefused(BodyBackupRunStatus status)
    {
        Assert.Throws<ArgumentException>(() => BodyBackupNote.For(new BodyBackupRunResult(status)));
    }

    [Fact]
    public void AnOutcomeTheWordingDoesNotKnowIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BodyBackupNote.For(new BodyRestoreResult((BodyRestoreOutcome)99)));
        Assert.Throws<ArgumentOutOfRangeException>(() => BodyBackupNote.For(new BodyBackupResult((BodyBackupOutcome)99)));
        Assert.Throws<ArgumentOutOfRangeException>(() => BodyBackupNote.For(new BodyBackupRunResult((BodyBackupRunStatus)99)));
    }

    [Fact]
    public void ANullResultIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => BodyBackupNote.For((BodyRestoreResult)null!));
        Assert.Throws<ArgumentNullException>(() => BodyBackupNote.For((BodyBackupResult)null!));
        Assert.Throws<ArgumentNullException>(() => BodyBackupNote.For((BodyBackupRunResult)null!));
    }
}
