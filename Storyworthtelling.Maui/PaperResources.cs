namespace Storyworthtelling.Maui;

/// <summary>
/// Reads a Paper &amp; Ink token out of the merged dictionaries.
///
/// <para>One place, because the rule is easy to get subtly wrong: a
/// <see cref="ResourceDictionary"/>'s indexer looks only at that dictionary's own entries, while
/// <c>TryGetValue</c> walks what has been merged into it — and everything the kit provides arrives
/// by merge. The indexer therefore appears to work while the app is small and starts throwing when
/// the kit is the only source left.</para>
///
/// <para>It throws rather than falling back to a default, and says what to do. A view drawn in
/// transparent-on-transparent is a bug that looks like a rendering problem for an afternoon.</para>
/// </summary>
public static class PaperResources
{
    public static T Token<T>(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is T typed
            ? typed
            : throw new InvalidOperationException(
                $"The Paper & Ink kit is not merged: no resource named '{key}'. A head adds "
                + "Resources.MergedDictionaries.Add(new PaperKit()) in its App constructor.");

    /// <summary>The colour under <paramref name="key"/>. The common case, so it has a name.</summary>
    public static Color Colour(string key) => Token<Color>(key);
}
