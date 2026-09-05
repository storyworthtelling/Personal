namespace Storyboarder.Domain.Text;

// Derives a book's title from the picked manuscript's Drive display name. The file the owner chose is the
// source of the title, so every draft and every book in a series is named from its OWN manuscript rather
// than inheriting a prior draft's or a prior book's title. Strips a known manuscript/document extension
// (native Google Docs already carry none) and trims surrounding whitespace; a blank name, or one that is
// nothing but an extension, falls back to the raw input so a title is never emptied.
public static class ManuscriptTitle
{
    // Only these trailing extensions are stripped. Using a known set (rather than Path.GetExtension alone)
    // avoids clobbering a legitimate title that happens to contain a dot, e.g. "Book 2.5".
    private static readonly string[] KnownExtensions =
        [".gdoc", ".docx", ".doc", ".rtf", ".epub", ".pdf", ".txt", ".md"];

    public static string FromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return fileName;

        var trimmed = fileName.Trim();
        var ext = System.IO.Path.GetExtension(trimmed);
        if (!string.IsNullOrEmpty(ext) && KnownExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            var withoutExt = trimmed[..^ext.Length].Trim();
            if (withoutExt.Length > 0)
                return withoutExt;
        }
        return trimmed;
    }
}
