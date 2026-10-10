using Pulp.Domain.FirstRun;
using Pulp.Domain.People;

namespace Pulp.Tests.FirstRun;

/// <summary>
/// First run (D142): the vault, then the household, then whose phone this is — each step done or
/// skipped, and the sequence picks up at the first one that is neither, so leaving half-way and
/// coming back resumes rather than starting over. The gate opens first run at launch only until it
/// has been finished once (no tour, D142).
/// </summary>
public class FirstRunPlanTests
{
    private static readonly FirstRunState Fresh = new(
        SignedIn: false, HasPassphrase: false, KeysPresent: 0, HasRoster: false,
        Phone: ThisPhoneStatus.NoRoster, Skipped: new HashSet<FirstRunStep>(), Finished: false);

    private static readonly FirstRunState Settled = Fresh with
    {
        SignedIn = true, HasPassphrase = true, KeysPresent = FirstRunPlan.KeyCount, HasRoster = true, Phone = ThisPhoneStatus.Known,
    };

    [Fact]
    public void AFreshPhoneStartsAtTheVault() =>
        Assert.Equal(FirstRunStep.Vault, FirstRunPlan.Next(Fresh));

    [Fact]
    public void ThePassphraseWithoutTheGoogleAccountIsNotEnough() =>
        Assert.Equal(FirstRunStep.Vault, FirstRunPlan.Next(Fresh with { HasPassphrase = true }));

    [Fact]
    public void AnOpenedVaultMissingAKeyStaysOnTheVaultStep() =>
        Assert.Equal(FirstRunStep.Vault, FirstRunPlan.Next(Settled with { KeysPresent = 2 }));

    [Fact]
    public void WithTheVaultOpenAndNoRosterTheHouseholdIsNext() =>
        Assert.Equal(FirstRunStep.Household, FirstRunPlan.Next(Settled with { HasRoster = false, Phone = ThisPhoneStatus.NoRoster }));

    [Fact]
    public void ARosterThatDoesNotSettleThePhoneAsksWhoseItIs() =>
        Assert.Equal(FirstRunStep.ThisPhone, FirstRunPlan.Next(Settled with { Phone = ThisPhoneStatus.Unmatched }));

    [Fact]
    public void EverythingInPlaceIsDone() =>
        Assert.Equal(FirstRunStep.Done, FirstRunPlan.Next(Settled));

    [Fact]
    public void ASkippedStepIsNotAskedAgain()
    {
        var skipped = Fresh with { Skipped = new HashSet<FirstRunStep> { FirstRunStep.Vault } };

        Assert.Equal(FirstRunStep.Household, FirstRunPlan.Next(skipped));
    }

    [Fact]
    public void WithNoRosterThereIsNobodyToMatchSoSkippingTheHouseholdFinishes()
    {
        var skipped = Fresh with { Skipped = new HashSet<FirstRunStep> { FirstRunStep.Vault, FirstRunStep.Household } };

        Assert.Equal(FirstRunStep.Done, FirstRunPlan.Next(skipped));
    }

    [Theory]
    [InlineData(FirstRunStep.Vault)]
    [InlineData(FirstRunStep.Household)]
    [InlineData(FirstRunStep.ThisPhone)]
    public void EveryRealStepCanBeSkipped(FirstRunStep step) => Assert.True(FirstRunPlan.CanSkip(step));

    [Fact]
    public void DoneIsNotAStepToSkip() => Assert.False(FirstRunPlan.CanSkip(FirstRunStep.Done));

    [Fact]
    public void TheGateOpensFirstRunOnAFreshPhone() => Assert.True(FirstRunGate.ShouldOpen(Fresh));

    [Fact]
    public void TheGateStaysShutOnceFirstRunWasFinished() =>
        Assert.False(FirstRunGate.ShouldOpen(Fresh with { Finished = true }));

    [Fact]
    public void TheGateStaysShutWhenEverythingIsInPlace() => Assert.False(FirstRunGate.ShouldOpen(Settled));

    /// <summary>
    /// At launch the keys are only in memory once the vault has been read, so a phone with its
    /// passphrase and account would look keyless before the refresh lands. The gate must not reopen
    /// first run on every launch for that; a missing key is the Vault page's to say.
    /// </summary>
    [Fact]
    public void TheGateDoesNotOpenForKeysAloneSinceTheyArriveAfterLaunch() =>
        Assert.False(FirstRunGate.ShouldOpen(Settled with { KeysPresent = 0 }));

    [Fact]
    public void TheGateResumesAPartlyDoneFirstRun() =>
        Assert.True(FirstRunGate.ShouldOpen(Settled with { HasRoster = false, Phone = ThisPhoneStatus.NoRoster }));
}
