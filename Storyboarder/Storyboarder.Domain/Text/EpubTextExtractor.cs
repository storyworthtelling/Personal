using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Storyboarder.Domain.Text;

// Extracts the reading-order text of an ePub. An ePub is a ZIP of XHTML, so this is pure BCL (no
// dependency): read META-INF/container.xml to find the OPF package, read the OPF's spine for the reading
// order, then pull the text out of each XHTML document (block tags become paragraph breaks, other tags
// are stripped, entities decoded). Lenient — a malformed part is skipped rather than failing the whole
// book — so a slightly non-standard ePub still yields readable prose.
public static class EpubTextExtractor
{
    public static string Extract(byte[] epub)
    {
        if (epub is not { Length: > 0 })
            return string.Empty;

        using var stream = new MemoryStream(epub, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var opfPath = FindOpfPath(archive);
        if (opfPath is null)
            return string.Empty;

        var opfEntry = archive.GetEntry(opfPath);
        if (opfEntry is null)
            return string.Empty;

        var opf = Load(opfEntry);
        if (opf is null)
            return string.Empty;

        var opfDir = GetDirectory(opfPath);

        // manifest: id -> href
        var manifest = Descendants(opf, "item")
            .Where(i => i.Attribute("id") is not null && i.Attribute("href") is not null)
            .GroupBy(i => i.Attribute("id")!.Value)
            .ToDictionary(g => g.Key, g => g.First().Attribute("href")!.Value, StringComparer.Ordinal);

        // spine: the reading order of manifest ids.
        var spine = Descendants(opf, "itemref")
            .Select(r => r.Attribute("idref")?.Value)
            .Where(id => id is not null && manifest.ContainsKey(id))
            .Select(id => manifest[id!])
            .ToList();

        var builder = new StringBuilder();
        foreach (var href in spine)
        {
            var path = CombineZipPath(opfDir, WebUtility.UrlDecode(href));
            var entry = archive.GetEntry(path);
            if (entry is null)
                continue;

            var xhtml = ReadEntry(entry);
            var text = StripHtml(xhtml);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (builder.Length > 0)
                    builder.Append("\n\n");
                builder.Append(text);
            }
        }

        return builder.ToString();
    }

    private static string? FindOpfPath(ZipArchive archive)
    {
        var container = archive.GetEntry("META-INF/container.xml");
        if (container is null)
            return archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))?.FullName;

        var doc = Load(container);
        var rootfile = doc is null ? null : Descendants(doc, "rootfile").FirstOrDefault();
        return rootfile?.Attribute("full-path")?.Value
            ?? archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))?.FullName;
    }

    private static XDocument? Load(ZipArchiveEntry entry)
    {
        try
        {
            using var reader = new StreamReader(entry.Open());
            return XDocument.Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            return null;
        }
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    // Match elements by local name, ignoring the XML namespace (container/OPF/XHTML each use their own).
    private static IEnumerable<XElement> Descendants(XDocument doc, string localName) =>
        doc.Descendants().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static string GetDirectory(string zipPath)
    {
        var slash = zipPath.LastIndexOf('/');
        return slash < 0 ? string.Empty : zipPath[..slash];
    }

    private static string CombineZipPath(string dir, string href) =>
        string.IsNullOrEmpty(dir) ? href : $"{dir}/{href}";

    private static readonly Regex BlockClose = new(
        @"</(p|div|h[1-6]|li|blockquote|section|article|br\s*/?)>|<br\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.CultureInvariant);
    private static readonly Regex Blanks = new(@"[ \t]+", RegexOptions.CultureInvariant);
    private static readonly Regex ExtraBlankLines = new(@"\n{3,}", RegexOptions.CultureInvariant);

    private static string StripHtml(string html)
    {
        // Drop the head/style/script content wholesale, turn block-closers into paragraph breaks, strip
        // remaining tags, decode entities, and tidy whitespace.
        html = Regex.Replace(html, @"<(head|style|script)[^>]*>.*?</\1>", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        html = BlockClose.Replace(html, "\n\n");
        html = AnyTag.Replace(html, string.Empty);
        html = WebUtility.HtmlDecode(html);
        html = Blanks.Replace(html, " ");
        html = ExtraBlankLines.Replace(html.Replace("\r", string.Empty), "\n\n");
        return html.Trim();
    }
}
