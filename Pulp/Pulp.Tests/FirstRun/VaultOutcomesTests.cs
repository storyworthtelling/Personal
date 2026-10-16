using Pulp.Domain;
using Pulp.Domain.Actions;
using Pulp.Domain.FirstRun;

namespace Pulp.Tests.FirstRun;

/// <summary>
/// What the Vault page and first run say after a restore or a save. Success is an inline state
/// change (a quiet sentence on the page); a failure is never on the page and never voiced — it folds
/// into the dog-ear as a Problem with the Vault as its one button, keyed so the next attempt
/// replaces it and a success withdraws it (DESIGN.md §13, D260).
/// </summary>
public class VaultOutcomesTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(VaultRestoreOutcome.WrongPassphrase)]
    [InlineData(VaultRestoreOutcome.Unreadable)]
    [InlineData(VaultRestoreOutcome.Failed)]
    public void ARestoreThatFailedIsANoticeNotASentence(VaultRestoreOutcome outcome)
    {
        Assert.Null(VaultOutcomes.Sentence(outcome, keysPresent: 0));
        var notice = VaultOutcomes.Notice(outcome, At);
        Assert.NotNull(notice);
        Assert.Equal(VaultOutcomes.NoticeKey, notice.Key);
        Assert.Equal(NoticeRole.Problem, notice.Role);
        Assert.Equal(NoticeAction.OpenVault, notice.Action);
    }

    [Theory]
    [InlineData(VaultRestoreOutcome.Opened)]
    [InlineData(VaultRestoreOutcome.NoVaultYet)]
    [InlineData(VaultRestoreOutcome.NotSignedIn)]
    [InlineData(VaultRestoreOutcome.NoPassphrase)]
    public void ARestoreThatDidNotFailIsASentenceNotANotice(VaultRestoreOutcome outcome)
    {
        Assert.Null(VaultOutcomes.Notice(outcome, At));
        Assert.False(string.IsNullOrWhiteSpace(VaultOutcomes.Sentence(outcome, keysPresent: 0)));
    }

    [Fact]
    public void AnOpenedVaultCountsTheKeysItHeld()
    {
        Assert.Equal("The vault is open, and all three keys came with it.", VaultOutcomes.Sentence(VaultRestoreOutcome.Opened, 3));
        Assert.Equal("The vault is open; 1 of the three keys came with it. Add the others in Vault.", VaultOutcomes.Sentence(VaultRestoreOutcome.Opened, 1));
    }

    [Theory]
    [InlineData(VaultSaveOutcome.Unreadable)]
    [InlineData(VaultSaveOutcome.WriteFailed)]
    [InlineData(VaultSaveOutcome.NotSignedIn)]
    [InlineData(VaultSaveOutcome.NoPassphrase)]
    public void ASaveThatDidNotLandIsANotice(VaultSaveOutcome outcome)
    {
        Assert.Null(VaultOutcomes.Sentence(outcome));
        var notice = VaultOutcomes.Notice(outcome, At);
        Assert.NotNull(notice);
        Assert.Equal(VaultOutcomes.NoticeKey, notice.Key);
        Assert.Equal(NoticeRole.Problem, notice.Role);
    }

    [Theory]
    [InlineData(VaultSaveOutcome.Saved)]
    [InlineData(VaultSaveOutcome.ReadOnly)]
    public void ASaveThatLandedOrIsNotThisPhonesToMakeIsASentence(VaultSaveOutcome outcome)
    {
        Assert.Null(VaultOutcomes.Notice(outcome, At));
        Assert.False(string.IsNullOrWhiteSpace(VaultOutcomes.Sentence(outcome)));
    }

    [Fact]
    public void ASignInThatDidNotCompleteIsANotice()
    {
        var notice = VaultOutcomes.SignInFailed(At);

        Assert.Equal(VaultOutcomes.SignInKey, notice.Key);
        Assert.Equal(NoticeRole.Problem, notice.Role);
    }

    [Fact]
    public void AnUnregisteredClientIsNamedAsSuchNotAsAFlakySignIn()
    {
        Assert.True(GoogleSignInFailure.IsUnregistered("android.accounts.AuthenticatorException: UnregisteredOnApiConsole"));
        Assert.False(GoogleSignInFailure.IsUnregistered("Account selection was cancelled."));
        Assert.False(GoogleSignInFailure.IsUnregistered(null));
    }

    [Fact]
    public void AnUnregisteredClientSaysWhatToRegisterAndDoesNotAskToRetry()
    {
        var notice = VaultOutcomes.SignInNotRegistered(At);

        Assert.Equal(VaultOutcomes.SignInKey, notice.Key);
        Assert.Equal(NoticeRole.Problem, notice.Role);
        Assert.Contains(PulpIdentity.ApplicationId, notice.Detail, StringComparison.Ordinal);
        Assert.Contains("SHA-1", notice.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("try signing in again", notice.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryOutcomeIsDecided()
    {
        var restores = Enum.GetValues<VaultRestoreOutcome>();
        var saves = Enum.GetValues<VaultSaveOutcome>();
        Assert.NotEmpty(restores);
        Assert.NotEmpty(saves);

        Assert.All(restores, outcome => Assert.True(VaultOutcomes.Sentence(outcome, 0) is not null ^ VaultOutcomes.Notice(outcome, At) is not null));
        Assert.All(saves, outcome => Assert.True(VaultOutcomes.Sentence(outcome) is not null ^ VaultOutcomes.Notice(outcome, At) is not null));
    }
}
