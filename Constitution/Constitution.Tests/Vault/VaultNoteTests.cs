using Constitution.Infrastructure.Vault;

namespace Constitution.Tests.Vault;

/// <summary>
/// S2 — the sentence left on a connection row after it is saved says what the vault did, not what was
/// hoped. Every way a run can end has its own words; none echoes a value or a passphrase, and an
/// outcome added later fails here until it is worded.
/// </summary>
public class VaultNoteTests
{
    private static ConstitutionVaultRunResult Published(VaultWriteOutcome shared, string? detail = null) =>
        new(VaultRunStatus.Published, Publish: new ConstitutionPublishResult(shared, VaultWriteOutcome.NothingToPublish, detail));

    [Fact]
    public void ALandedPublishIsTheCurrentSentence()
    {
        Assert.Equal(
            "Saved to the vault. It is checked on the next reading.",
            VaultNote.For(Published(VaultWriteOutcome.Published), VaultNoteSubject.SleepNumber));
        Assert.Equal(
            "Paired and saved to the vault. It is checked on the next delivery.",
            VaultNote.For(Published(VaultWriteOutcome.Published), VaultNoteSubject.Remarkable));
    }

    [Fact]
    public void WithoutAPassphraseItIsSavedOnThePhoneAndSaysHowToKeepIt()
    {
        var result = new ConstitutionVaultRunResult(VaultRunStatus.NoPassphrase);

        Assert.Equal(
            "Saved on this phone. Add the vault passphrase to keep it in the vault.",
            VaultNote.For(result, VaultNoteSubject.SleepNumber));
        Assert.Equal(
            "Paired and saved on this phone. Add the vault passphrase to keep it in the vault.",
            VaultNote.For(result, VaultNoteSubject.Remarkable));
    }

    [Fact]
    public void WithoutASignInItIsSavedOnThePhoneAndSaysHowToKeepIt()
    {
        var result = new ConstitutionVaultRunResult(VaultRunStatus.NotSignedIn);

        Assert.Equal(
            "Saved on this phone. Sign in with Google to keep it in the vault.",
            VaultNote.For(result, VaultNoteSubject.SleepNumber));
        Assert.Equal(
            "Paired and saved on this phone. Sign in with Google to keep it in the vault.",
            VaultNote.For(result, VaultNoteSubject.Remarkable));
    }

    [Fact]
    public void ARunThatFailedNamesTheKindOfFailureAndNothingElse()
    {
        var result = new ConstitutionVaultRunResult(VaultRunStatus.Failed, Detail: "TaskCanceledException");

        var note = VaultNote.For(result, VaultNoteSubject.SleepNumber);

        Assert.StartsWith("Saved on this phone, but the vault could not be updated", note, StringComparison.Ordinal);
        Assert.Contains("TaskCanceledException", note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(VaultWriteOutcome.RefusedUnreadable, "does not open")]
    [InlineData(VaultWriteOutcome.RefusedNotOwner, "another Google account")]
    [InlineData(VaultWriteOutcome.RefusedUnknownAccount, "Sign in with Google")]
    [InlineData(VaultWriteOutcome.Failed, "Drive")]
    [InlineData(VaultWriteOutcome.NothingToPublish, "nothing new")]
    [InlineData(VaultWriteOutcome.NoPassphrase, "passphrase")]
    public void EachRefusalIsToldInItsOwnWords(VaultWriteOutcome outcome, string fragment)
    {
        var note = VaultNote.For(Published(outcome), VaultNoteSubject.SleepNumber);

        Assert.StartsWith("Saved on this phone", note, StringComparison.Ordinal);
        Assert.Contains(fragment, note, StringComparison.Ordinal);
        Assert.DoesNotContain("Saved to the vault", note, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryWriteOutcomeHasWordsAndNoTwoShareThem()
    {
        var notes = Enum.GetValues<VaultWriteOutcome>()
            .Select(outcome => VaultNote.For(Published(outcome), VaultNoteSubject.SleepNumber))
            .ToList();

        Assert.All(notes, note =>
        {
            Assert.False(string.IsNullOrWhiteSpace(note));
            Assert.EndsWith(".", note, StringComparison.Ordinal);
        });
        Assert.Equal(notes.Count, notes.Distinct().Count());
    }

    [Fact]
    public void OnlyALandedPublishClaimsTheVaultHasIt()
    {
        var claims = new List<VaultWriteOutcome>();
        foreach (var outcome in Enum.GetValues<VaultWriteOutcome>())
        {
            var note = VaultNote.For(Published(outcome), VaultNoteSubject.Remarkable);
            if (note.Contains("saved to the vault", StringComparison.OrdinalIgnoreCase))
            {
                claims.Add(outcome);
            }
        }

        Assert.Equal([VaultWriteOutcome.Published], claims);
    }

    [Fact]
    public void AFailedWriteNamesTheFailureTypeNotItsMessage()
    {
        var detail = "HttpRequestException: could not reach " + Invented.Passphrase + " " + Invented.SleepWord;

        var note = VaultNote.For(Published(VaultWriteOutcome.Failed, detail), VaultNoteSubject.SleepNumber);

        Assert.Contains("HttpRequestException", note, StringComparison.Ordinal);
        Assert.DoesNotContain(Invented.Passphrase, note, StringComparison.Ordinal);
        Assert.DoesNotContain(Invented.SleepWord, note, StringComparison.Ordinal);
        Assert.DoesNotContain("could not reach", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureThatKeptNoTypeNameRepeatsNothingOfItsMessage()
    {
        // The service keeps only the message of a failed write; the message is the part that could carry a value.
        var note = VaultNote.For(Published(VaultWriteOutcome.Failed, "refused " + Invented.SleepWord), VaultNoteSubject.SleepNumber);

        Assert.DoesNotContain(Invented.SleepWord, note, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", note, StringComparison.Ordinal);
        Assert.StartsWith("Saved on this phone, but Drive did not take the update", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ARestoreIsNotAPublishAndIsRefused()
    {
        var restored = new ConstitutionVaultRunResult(VaultRunStatus.Restored);

        Assert.Throws<ArgumentException>(() => VaultNote.For(restored, VaultNoteSubject.SleepNumber));
    }

    [Fact]
    public void APublishWithoutItsResultIsRefused()
    {
        var empty = new ConstitutionVaultRunResult(VaultRunStatus.Published);

        Assert.Throws<ArgumentException>(() => VaultNote.For(empty, VaultNoteSubject.SleepNumber));
    }

    [Fact]
    public void EveryRunStatusIsHandled()
    {
        // A status added later must be worded (or refused) rather than fall through to a default.
        foreach (var status in Enum.GetValues<VaultRunStatus>())
        {
            var result = new ConstitutionVaultRunResult(
                status,
                Publish: status == VaultRunStatus.Published
                    ? new ConstitutionPublishResult(VaultWriteOutcome.Published, VaultWriteOutcome.Published)
                    : null,
                Detail: status == VaultRunStatus.Failed ? "IOException" : null);

            var outcome = Record.Exception(() => VaultNote.For(result, VaultNoteSubject.SleepNumber));

            if (status == VaultRunStatus.Restored)
            {
                Assert.IsType<ArgumentException>(outcome);
            }
            else
            {
                Assert.Null(outcome);
            }
        }
    }
}
