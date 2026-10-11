namespace Pulp.Domain.Settings;

/// <summary>
/// What someone has half-typed into a page's fields, by field, kept while the page rebuilds its views.
/// A field that has no draft shows its fallback; an emptied field is no draft.
/// </summary>
public sealed class PageDrafts
{
    private readonly Dictionary<string, string> drafts = [];

    public bool Any => drafts.Count > 0;

    public string Get(string field, string fallback) =>
        drafts.TryGetValue(field, out var text) ? text : fallback;

    public void Set(string field, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            drafts.Remove(field);
        }
        else
        {
            drafts[field] = text;
        }
    }

    public void Clear(string field) => drafts.Remove(field);
}

/// <summary>Why a page is re-reading the household.</summary>
public enum ReloadCause
{
    Opened,
    OwnWrites,
    Sync,
}

/// <summary>
/// Whether a re-read should hold off. Only a sync pass waits — it is nobody's doing, and rebuilding
/// the views under someone typing drops their cursor and keyboard; it runs when they leave the field.
/// A re-read after the person's own writes proceeds, because their drafts are kept across the rebuild.
/// </summary>
public static class ReloadPolicy
{
    public static bool ShouldWait(ReloadCause cause, bool fieldFocused, bool hasDraft) =>
        cause == ReloadCause.Sync && (fieldFocused || hasDraft);
}
