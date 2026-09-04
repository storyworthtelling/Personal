using System.Text;
using System.Text.RegularExpressions;

namespace Codex.Domain.Post;

/// <summary>
/// Makes an HTML-only email readable. <b>A stopgap, deliberately.</b>
///
/// Post has no HTML renderer: the hardened WebView is still Flyleaf's alone, and sharing it
/// is deferred work. Meanwhile a message whose only body is <c>text/html</c> has a null
/// plain-text part, so without this it lists, opens, and shows the owner an empty page —
/// with nothing in the log, because nothing failed. That is a worse outcome than crude
/// text, and it is invisible.
///
/// This is not an HTML renderer and does not attempt to be one. It drops what is not prose,
/// turns block elements into line breaks, decodes the handful of entities that actually
/// appear, and collapses the acres of whitespace marketing HTML is made of. It goes when
/// the shared web view lands.
/// </summary>
public static partial class HtmlToText
{
    // Whole elements whose CONTENT is not prose. Marketing mail carries kilobytes of CSS
    // that would otherwise become the entire message body.
    [GeneratedRegex(@"<(script|style|head|title)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NonProse();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"</(p|div|tr|li|h[1-6]|table|blockquote)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    // Anything else in angle brackets. The trailing `[^<]*$` case matters: real mail is not
    // well-formed, and a naive stripper meeting an unclosed tag swallows the rest of the
    // message, which looks exactly like a blank email.
    [GeneratedRegex(@"<[^>]*>|<[^<]*$", RegexOptions.Singleline)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t]*\n[ \t]*(\n[ \t]*)+")]
    private static partial Regex BlankRuns();

    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = NonProse().Replace(html, string.Empty);
        text = LineBreak().Replace(text, "\n");
        text = BlockEnd().Replace(text, "\n");
        text = AnyTag().Replace(text, string.Empty);
        text = Decode(text);
        text = BlankRuns().Replace(text, "\n\n");

        return text.Trim();
    }

    // Only the entities that actually turn up. A full table would be a dependency's job,
    // and this is a stopgap.
    private static string Decode(string text)
    {
        var decoded = new StringBuilder(text)
            .Replace("&nbsp;", " ")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&apos;", "'")
            .Replace("&mdash;", "—")
            .Replace("&ndash;", "–")
            .Replace("&hellip;", "…");

        // Ampersand LAST, or "&amp;lt;" decodes twice and becomes a stray bracket.
        return decoded.Replace("&amp;", "&").ToString();
    }
}
