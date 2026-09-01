namespace Storyworthtelling.Updates.Tests;

public class InstallDutyTests
{
    // The locked phone's launcher: device owner, and therefore the only thing on that
    // device that can install anything at all.
    [Fact]
    public void TheDeviceOwnerIsTheCustodian()
    {
        Assert.Equal(
            UpdateRole.Custodian,
            InstallDuty.Decide(isSelfDeviceOwner: true, custodianIsPresent: true));
    }

    // An ordinary Android phone with no device owner: every app installs itself,
    // through the confirmed installer, because nothing else is going to.
    [Fact]
    public void WithNoCustodianAnAppLooksAfterItself()
    {
        Assert.Equal(
            UpdateRole.SelfOnly,
            InstallDuty.Decide(isSelfDeviceOwner: false, custodianIsPresent: false));
    }

    // The case that did not exist before, and the one that matters: a sibling app on
    // the LOCKED phone. It must not try. It cannot install silently — it is not the
    // device owner — and unknown-sources stays off, so an attempt is a guaranteed
    // failure that would show up as a permanent error line on a phone whose updates
    // are in fact working perfectly.
    [Fact]
    public void OnTheLockedPhoneASiblingAppDefersToTheCustodian()
    {
        Assert.Equal(
            UpdateRole.Deferred,
            InstallDuty.Decide(isSelfDeviceOwner: false, custodianIsPresent: true));
    }

    // Being the device owner settles it whatever else is on the phone: the question
    // "can I install" is answered by what I am, not by who else is here.
    [Fact]
    public void BeingTheDeviceOwnerOutranksEverything()
    {
        Assert.Equal(
            UpdateRole.Custodian,
            InstallDuty.Decide(isSelfDeviceOwner: true, custodianIsPresent: false));
    }
}

public class DeferredScopeTests
{
    private static readonly InstallCandidate Score =
        new("com.storyworthtelling.focusphone.audio", 200);

    private static readonly InstallCandidate Launcher =
        new("com.storyworthtelling.focusphone.launcher", 200);

    // Deferred installs nothing — not even itself. Anything else and the sibling app
    // on the locked phone would retry a download it can never install.
    [Fact]
    public void Deferred_InstallsNothingAtAll()
    {
        var chosen = UpdateScope.Decide(
            UpdateRole.Deferred,
            Score.Package,
            [Score, Launcher],
            new Dictionary<string, long>());

        Assert.Empty(chosen);
    }
}
