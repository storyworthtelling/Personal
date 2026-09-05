namespace Storyworthtelling.Updates;

/// <summary>
/// Which <see cref="UpdateRole"/> an app has on the phone it is actually running on.
///
/// Separate from <see cref="UpdateScope"/> because it answers a different question.
/// UpdateScope asks "given my role, which APKs do I install?"; this asks "what is my
/// role here?", and the answer is a fact about the device rather than about the app.
/// The same Score build is a self-updater on an unrestricted phone and installs
/// nothing at all on the locked one, and neither is a setting anybody should have to
/// get right.
/// </summary>
public static class InstallDuty
{
    /// <param name="isSelfDeviceOwner">
    /// Whether this app is the device owner — the only thing that can install
    /// silently, which is what makes it the custodian.
    /// </param>
    /// <param name="custodianIsPresent">
    /// Whether any app in the suite holds device owner on this phone. On the locked
    /// phone that is the launcher; on an ordinary Android phone nothing does.
    /// </param>
    public static UpdateRole Decide(bool isSelfDeviceOwner, bool custodianIsPresent)
    {
        // Being the device owner settles it whatever else is installed: whether I can
        // install is answered by what I am, not by who else is here.
        if (isSelfDeviceOwner)
        {
            return UpdateRole.Custodian;
        }

        return custodianIsPresent ? UpdateRole.Deferred : UpdateRole.SelfOnly;
    }
}
