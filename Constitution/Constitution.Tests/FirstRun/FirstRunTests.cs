using Constitution.Domain.FirstRun;

namespace Constitution.Tests.FirstRun;

/// <summary>
/// C-8.3.1 (brief First run): a fresh install asks for exactly three things, in order. Health Connect,
/// Sleep Number and the reMarkable are asked when first needed or restored from the vault, never here.
/// </summary>
public class FirstRunTests
{
    private static readonly FirstRunState Fresh = new(false, false, false);

    [Fact]
    public void ThreeStepsOnly()
    {
        Assert.Equal(
            [FirstRunStep.GoogleSignIn, FirstRunStep.VaultPassphrase, FirstRunStep.Equipment],
            FirstRunModel.Steps);
        Assert.Equal(3, FirstRunModel.Steps.Count);
        Assert.Equal(3, Enum.GetValues<FirstRunStep>().Length);
    }

    [Fact]
    public void NoStepAsksForAConnectionThatIsAskedWhenFirstNeeded()
    {
        var names = Enum.GetNames<FirstRunStep>().Concat(FirstRunModel.Steps.Select(step => step.ToString())).ToList();

        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, name =>
            name.Contains("Health", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Sleep", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Remarkable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFreshInstallStartsAtSignIn()
    {
        Assert.Equal(FirstRunStep.GoogleSignIn, FirstRunModel.Current(Fresh));
        Assert.False(FirstRunModel.IsDone(Fresh));
    }

    [Fact]
    public void StepsComeInOrder()
    {
        Assert.Equal(FirstRunStep.VaultPassphrase, FirstRunModel.Current(Fresh with { SignedIn = true }));
        Assert.Equal(FirstRunStep.Equipment, FirstRunModel.Current(new FirstRunState(true, true, false)));
    }

    [Fact]
    public void AStepAlreadySatisfiedIsSkipped()
    {
        // A restore can satisfy the passphrase while sign-in is still owed: sign-in stays first.
        Assert.Equal(FirstRunStep.GoogleSignIn, FirstRunModel.Current(new FirstRunState(false, true, true)));
        // Signed in with a passphrase already held skips straight to equipment.
        Assert.Equal(FirstRunStep.Equipment, FirstRunModel.Current(new FirstRunState(true, true, false)));
        // Equipment confirmed (restored) but no passphrase yet: the passphrase is still owed.
        Assert.Equal(FirstRunStep.VaultPassphrase, FirstRunModel.Current(new FirstRunState(true, false, true)));
    }

    [Fact]
    public void DoneOnlyWhenAllThreeAreSatisfied()
    {
        Assert.True(FirstRunModel.IsDone(new FirstRunState(true, true, true)));
        Assert.Null(FirstRunModel.Current(new FirstRunState(true, true, true)));
        foreach (var state in new[]
        {
            new FirstRunState(false, true, true),
            new FirstRunState(true, false, true),
            new FirstRunState(true, true, false),
        })
        {
            Assert.False(FirstRunModel.IsDone(state));
            Assert.NotNull(FirstRunModel.Current(state));
        }
    }

    [Fact]
    public void EachStepHasAStatusSentenceAndAnActionLabel()
    {
        foreach (var step in FirstRunModel.Steps)
        {
            Assert.False(string.IsNullOrWhiteSpace(FirstRunModel.Sentence(step)));
            Assert.False(string.IsNullOrWhiteSpace(FirstRunModel.ActionLabel(step)));
        }
    }

    [Fact]
    public void ConfirmationIsExplicitBecauseEquipmentDefaultsToAllTicked()
    {
        // The stored equipment reads as everything when never asked, so a state with nothing
        // confirmed must still owe the equipment step even once sign-in and passphrase are held.
        Assert.Equal(FirstRunStep.Equipment, FirstRunModel.Current(new FirstRunState(true, true, false)));
    }
}
