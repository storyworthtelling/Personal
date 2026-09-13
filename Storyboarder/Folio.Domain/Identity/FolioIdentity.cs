namespace Folio.Domain.Identity;

/// <summary>
/// The identifiers that survive the rename, in one place.
///
/// <para>P10 renames Canonizer to Folio — folders, namespaces, projects, the solution, the
/// database, the on-device label. None of that may move the identity of the <i>installed</i>
/// app, because a phone recognises an update only when the package name is unchanged, and the
/// suite's release channel installs by package id. So this string still says
/// <c>storyboarder</c>, and always will. It is the app's address, not a description of what
/// it does.</para>
///
/// <para>One constant rather than a copy per attribute: P8 found the Plate id written into
/// five files with only one of them guarded, so four could drift in silence. Folio carries the
/// id in an attribute on the worker and an attribute on the launcher activity — the pair that
/// must not be able to disagree, since each one is read by a different part of the system and
/// neither failure says anything out loud.</para>
///
/// <para>It lives in Domain because Domain has no dependencies and every layer can see it,
/// including the desktop test build — which is what lets the guards run with no Android
/// workload installed.</para>
/// </summary>
public static class FolioIdentity
{
    /// <summary>
    /// Frozen forever. Renaming it would present the app to the release channel as something
    /// new: the phone would keep the old one installed, never see another update, and go on
    /// holding the owner's books in the copy that no longer moves.
    /// </summary>
    public const string ApplicationId = "com.storyworthtelling.storyboarder";

    /// <summary>The name the owner sees — on the phone, and on the foot of the Settings page.</summary>
    public const string DisplayName = "Folio";
}
