using Index.Domain.Household;
using Xunit;

namespace Index.Tests.Household;

/// <summary>
/// Task 2.7 of the remediation plan (mockups 10 and 11): Settings has no Save bar, every field saves
/// itself, Accounts refreshes when it opens, and her Settings says what mockup 11 says. The Android head
/// cannot be loaded by this desktop-TFM project, so each decision it makes is a pure Domain function
/// tested here, and <c>SettingsRefreshSourceTests</c> reads the head to prove it obeys them.
/// </summary>
public class SettingsSelfSaveTests
{
    // ---- a field commits when it is left, and only when it changed (finding 1) ----

    [Fact]
    public void AnUnchangedFieldCommitsNothing()
    {
        var plan = SettingsCommitPlan.Decide("id", "secret", "pass", "id", "secret", "pass");

        Assert.False(plan.Any);
        Assert.False(plan.StorePlaid);
        Assert.False(plan.StorePassphrase);
    }

    [Fact]
    public void AChangedPassphraseCommitsTheStorePassphraseAlone()
    {
        var plan = SettingsCommitPlan.Decide("id", "secret", "old", "id", "secret", "new");

        Assert.True(plan.StorePassphrase);
        Assert.False(plan.StorePlaid);
    }

    [Fact]
    public void AChangedPairCommitsPlaidAlone()
    {
        var plan = SettingsCommitPlan.Decide("id", "secret", "pass", "id2", "secret2", "pass");

        Assert.True(plan.StorePlaid);
        Assert.False(plan.StorePassphrase);
    }

    [Fact]
    public void ThePairIsTrimmedBeforeItIsCompared()
    {
        // Leaving a field the owner pasted with a trailing space is not a change.
        var plan = SettingsCommitPlan.Decide("id", "secret", "pass", " id ", "secret\n", "pass");

        Assert.False(plan.Any);
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("id", "")]
    [InlineData("", "")]
    [InlineData("   ", "secret")]
    public void AHalfTypedPairNeverReplacesWorkingCredentials(string typedId, string typedSecret)
    {
        // Replace clears both boxes; leaving one of them (or both) empty must not blank the stored pair
        // that is still working, or a blur would do what a mis-tap on Save never could.
        var plan = SettingsCommitPlan.Decide("id", "secret", "pass", typedId, typedSecret, "pass");

        Assert.False(plan.StorePlaid);
    }

    [Fact]
    public void AFreshPairOnAnInstallWithNoCredentialsCommits()
    {
        var plan = SettingsCommitPlan.Decide("", "", "", "id", "secret", "");

        Assert.True(plan.StorePlaid);
    }

    [Fact]
    public void ClearingThePassphraseIsAChange()
    {
        var plan = SettingsCommitPlan.Decide("id", "secret", "pass", "id", "secret", "");

        Assert.True(plan.StorePassphrase);
    }

    [Fact]
    public void ANullFieldIsTreatedAsEmpty()
    {
        var plan = SettingsCommitPlan.Decide("", "", "", null, null, null);

        Assert.False(plan.Any);
    }

    // ---- refresh on open (D149, I5) ----

    [Fact]
    public void AnOwnerWithKeysAndNothingRunningRefreshes()
    {
        Assert.True(RefreshOnOpen.ShouldStart(InstallRole.Owner, isConfigured: true, refreshActive: false));
    }

    [Theory]
    [InlineData(InstallRole.Hers, true, false)]
    [InlineData(InstallRole.Hers, false, false)]
    [InlineData(InstallRole.Owner, false, false)]
    [InlineData(InstallRole.Owner, true, true)]
    [InlineData(InstallRole.Hers, true, true)]
    public void HersAnUnconfiguredInstallAndARunningRefreshNeverStartAnother(
        InstallRole role, bool configured, bool active)
    {
        Assert.False(RefreshOnOpen.ShouldStart(role, configured, active));
    }

    [Fact]
    public void TheStatusSentenceIsTheMockupsWithOneEllipsisCharacter()
    {
        Assert.Equal("Refreshing balances…", RefreshOnOpen.RefreshingSentence);
    }

    private static readonly DateTime Now = new(2026, 10, 3, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ARequestedRefreshIsActiveWhileTheCycleRuns()
    {
        Assert.True(RefreshOnOpen.IsActive(Now.AddMinutes(-3), Now, cycleRunning: true, lastRunAtUtc: DateTime.MinValue));
    }

    [Fact]
    public void ARequestedRefreshIsActiveDuringTheGraceBeforeTheWorkerStarts()
    {
        Assert.True(RefreshOnOpen.IsActive(
            Now.AddSeconds(-5), Now, cycleRunning: false, lastRunAtUtc: Now.AddHours(-2)));
    }

    [Fact]
    public void ARequestedRefreshStopsBeingActiveOnceTheCycleHasRecordedItsEnd()
    {
        Assert.False(RefreshOnOpen.IsActive(
            Now.AddSeconds(-30), Now, cycleRunning: false, lastRunAtUtc: Now.AddSeconds(-2)));
    }

    [Fact]
    public void ARequestThatNeverStartedIsAbandonedAfterTheGraceInsteadOfClaimingToRefreshForever()
    {
        // Offline, or the keys were removed: WorkManager never ran the cycle. The sentence must not
        // outlive the grace, or it says "Refreshing" for the whole poll window about nothing.
        Assert.False(RefreshOnOpen.IsActive(
            Now.AddSeconds(-(RefreshOnOpen.StartGrace.TotalSeconds + 1)), Now,
            cycleRunning: false, lastRunAtUtc: Now.AddHours(-2)));
    }

    [Fact]
    public void WithNoRequestOnlyARunningCycleIsActive()
    {
        Assert.False(RefreshOnOpen.IsActive(null, Now, cycleRunning: false, lastRunAtUtc: Now.AddHours(-2)));
        Assert.True(RefreshOnOpen.IsActive(null, Now, cycleRunning: true, lastRunAtUtc: Now.AddHours(-2)));
    }

    // ---- "Refreshed 2h ago", not "Refreshed — 2h ago" (mockups 2 and 10) ----

    [Fact]
    public void ASuccessfulRunReadsRefreshedTwoHoursAgoWithNoDash()
    {
        var now = new DateTime(2026, 10, 3, 15, 0, 0);

        var summary = RefreshOnOpen.Summary("Refreshed", now.AddHours(-2), now);

        Assert.Equal("Refreshed 2h ago", summary);
        Assert.DoesNotContain("—", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunThatFinishedASecondAgoReadsRefreshedJustNow()
    {
        var now = new DateTime(2026, 10, 3, 15, 0, 0);

        Assert.Equal("Refreshed just now", RefreshOnOpen.Summary("Refreshed", now.AddSeconds(-1), now));
    }

    [Fact]
    public void ARunThatNeverHappenedSaysSo()
    {
        Assert.Equal("Not refreshed yet", RefreshOnOpen.Summary(string.Empty, DateTime.MinValue, DateTime.Now));
        Assert.Equal("Not refreshed yet", RefreshOnOpen.Summary("Refreshed", DateTime.MinValue, DateTime.Now));
    }

    [Fact]
    public void AnOutcomeThatIsNotSuccessKeepsItsOwnWordsBeforeTheTime()
    {
        var now = new DateTime(2026, 10, 3, 15, 0, 0);

        var summary = RefreshOnOpen.Summary("Failed: timed out", now.AddHours(-2), now);

        Assert.StartsWith("Failed: timed out", summary, StringComparison.Ordinal);
        Assert.EndsWith("2h ago", summary, StringComparison.Ordinal);
    }

    // ---- her Settings (mockup 11) and the owner's, which this task does not touch ----

    [Fact]
    public void HerSettingsSaysWhatMockupElevenSays()
    {
        Assert.Equal("Vault", SettingsCopy.VaultHeading(InstallRole.Hers));
        Assert.Equal(
            "The same passphrase as on the other phone. It opens the balances Index shares with this one.",
            SettingsCopy.PassphraseNote(InstallRole.Hers));
        Assert.Equal("Signed in", SettingsCopy.SignedInHeading);
        Assert.Equal(
            "her@example.com · Drive, where the shared balances and the voice are read",
            SettingsCopy.SignedInLine("her@example.com"));
    }

    [Fact]
    public void HerSettingsHasNoIntroAboveTheFieldAndNoGoogleSalesPitch()
    {
        Assert.Null(SettingsCopy.VaultIntro(InstallRole.Hers));
        Assert.Null(SettingsCopy.GoogleIntro(InstallRole.Hers));
    }

    [Fact]
    public void NothingOnHerScreenMentionsPlaidOrTheOwnersBanksRestoring()
    {
        var everything = new[]
        {
            SettingsCopy.VaultHeading(InstallRole.Hers),
            SettingsCopy.VaultIntro(InstallRole.Hers) ?? string.Empty,
            SettingsCopy.PassphraseNote(InstallRole.Hers),
            SettingsCopy.GoogleIntro(InstallRole.Hers) ?? string.Empty,
            SettingsCopy.SignedInHeading,
            SettingsCopy.SignedInLine("her@example.com"),
        };

        foreach (var text in everything)
        {
            Assert.DoesNotContain("Plaid", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("factory reset", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("restore automatically", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access token", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheOwnersVaultCopyIsStillTheCopyItWasBeforeThisTask()
    {
        // The owner-side drift (mockup 10's "Vault" heading and helper) is Phase 4, not this task.
        Assert.Equal("Secrets vault", SettingsCopy.VaultHeading(InstallRole.Owner));
        Assert.Contains("your Plaid credentials", SettingsCopy.VaultIntro(InstallRole.Owner), StringComparison.Ordinal);
        Assert.StartsWith("Shown in the clear", SettingsCopy.PassphraseNote(InstallRole.Owner), StringComparison.Ordinal);
        Assert.StartsWith("Sign in with Google", SettingsCopy.GoogleIntro(InstallRole.Owner), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(InstallRole.Hers, false, true)]
    [InlineData(InstallRole.Hers, true, false)]
    [InlineData(InstallRole.Owner, false, true)]
    [InlineData(InstallRole.Owner, true, true)]
    public void HerSignInButtonIsShownOnlyWhileSignedOut(InstallRole role, bool signedIn, bool shown)
    {
        Assert.Equal(shown, SettingsCopy.ShowsSignIn(role, signedIn));
    }
}
