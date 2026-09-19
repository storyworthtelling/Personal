namespace Folio.Domain.Imaging;

/// <summary>
/// Which drafts' rendered scene images may be freed when a draft is archived, and which may not.
///
/// <para><b>Why this is a decision and not a delete.</b> "Free the local copies after archiving" used
/// to free the archiving draft's own scenes, which is correct about that draft's report — it is
/// composed and delivered by then — and wrong about every draft after it. A changed scene prints a
/// before and after (F41), and the <i>before</i> is the previous draft's rendered image: the only
/// copy of it, held nowhere else. Freeing it does not fail anything. The pair simply prints one
/// panel, and the cross-draft read the whole phase exists for quietly stops working.</para>
///
/// <para>So a pair reaches back exactly one draft, and two drafts are kept: the one being archived,
/// because the <i>next</i> draft will pair against it, and the one before it, because this draft's
/// own diff still shows it. Everything older is genuinely finished and is freed — which is what the
/// setting was for.</para>
/// </summary>
public static class SceneRetention
{
    /// <summary>
    /// The drafts of a project whose scene images can go when <paramref name="archiving"/> is
    /// archived. Order does not matter; the ids are the folder names the store keeps.
    /// </summary>
    public static IReadOnlyList<Guid> Freeable(
        Guid archiving, Guid? previousDraft, IReadOnlyList<Guid> projectDrafts)
    {
        ArgumentNullException.ThrowIfNull(projectDrafts);

        return [.. projectDrafts.Where(draft => draft != archiving && draft != previousDraft).Distinct()];
    }
}
