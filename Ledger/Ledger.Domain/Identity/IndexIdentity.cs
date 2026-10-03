namespace Index.Domain.Identity;

/// <summary>
/// The identifiers that survive every rename to come, in one place.
///
/// <para>P11 renames Ledger to Index under a brand-new package id (D01) — there is no existing
/// install to keep alive across this particular change, since the old <c>com.storyworthtelling.ledger</c>
/// package is retired rather than upgraded. But from Index's own first ship onward, these strings
/// are frozen exactly the way Foreword's and Folio's are: a phone recognises an update only when
/// the package name is unchanged, and the device-owner update channel installs by package id. So
/// once this ships, nothing here may move again.</para>
///
/// <para>It lives in Domain because Domain has no dependencies and every layer can see it,
/// including the desktop test build — which is what lets the guards run with no Android
/// workload installed.</para>
/// </summary>
public static class IndexIdentity
{
    /// <summary>
    /// Frozen from Index's first ship onward. Renaming it would present the app to the update
    /// channel as something new: the phone would keep the old one installed and never see
    /// another update.
    /// </summary>
    public const string ApplicationId = "com.storyworthtelling.index";

    /// <summary>The name the owner sees — on the phone, and on the foot of the Settings page.</summary>
    public const string DisplayName = "Index";
}
