namespace Index.Domain.Identity;

/// <summary>
/// The identifiers that survive every rename to come, in one place.
///
/// <para>P11 renames Ledger to Index, and keeps the <c>com.storyworthtelling.ledger</c> package id
/// (which supersedes the plan's D01, a brand-new id). A phone recognises an update only when the
/// package name is unchanged, and the device-owner update channel installs by package id, so a new
/// id put Index beside the old app as a second install — and left Google's Android sign-in, which is
/// registered against the package name, pointing at an app that no longer existed. Foreword, Folio,
/// Post and Plate all kept their ids through their renames; Index now does the same. These strings
/// are frozen exactly the way theirs are, and nothing here may move again.</para>
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
    public const string ApplicationId = "com.storyworthtelling.ledger";

    /// <summary>The name the owner sees — on the phone, and on the foot of the Settings page.</summary>
    public const string DisplayName = "Index";
}
