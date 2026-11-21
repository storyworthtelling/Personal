namespace Storyworthtelling.Updates;

/// <summary>
/// The rolling GitHub releases the update channel reads, one per form (P14 release A fix round, finding 1). Every Flyleaf
/// and Score already on a phone lists the phone's release by <c>.apk</c> and installs by package, whatever the form, so
/// a watch APK published there would replace its phone app (the Point installer replacing Flyleaf on the locked phone for
/// good) and the face would land on both phones. Watch APKs therefore go only to a release of their own, which nothing on
/// a phone has ever read, and each release is read by its tag, never as "the latest". Wire words: frozen, and literals,
/// never <c>nameof</c>.
/// </summary>
public static class ReleaseChannels
{
    /// <summary>The phone's release: every phone APK, and nothing else. The publish action's default.</summary>
    public const string Phone = "apps-latest";

    /// <summary>The watch's release: every watch APK, a prerelease that is never the repository's latest. Read only by
    /// Flyleaf's watch half, which sends what it carries to the Point installer.</summary>
    public const string Watch = "watch-latest";

    /// <summary>The release a form's APKs are published to and read from; none for an unknown form.</summary>
    public static string? For(ApkForm form) => form switch
    {
        ApkForm.Phone => Phone,
        ApkForm.Watch => Watch,
        _ => null,
    };
}
