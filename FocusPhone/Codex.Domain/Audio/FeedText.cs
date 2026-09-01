using System.Net;
using System.Text;

namespace Codex.Domain.Audio;

/// <summary>
/// Turns the HTML that podcast feeds put in &lt;description&gt;, &lt;itunes:summary&gt;
/// and &lt;content:encoded&gt; into the plain prose the Notes panel renders.
///
/// Feeds are inconsistent to the point of hostility here: the same show may send
/// entity-escaped markup, real markup, or bare text, and the three fields often
/// disagree with each other. So this is deliberately forgiving rather than a
/// parser — it drops tags, keeps the line breaks the block tags implied, decodes
/// entities, and collapses the whitespace that falls out of doing so. Markup with
/// no words in it comes back as null, because an empty panel should read as
/// "this show publishes no notes" rather than as a blank page.
/// </summary>
public static class FeedText
{
    // The tags that end a line of prose. Everything else is inline and simply drops.
    private static readonly string[] BreakingTags =
        ["p", "br", "div", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote"];

    public static string? ToPlainText(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup))
        {
            return null;
        }

        var stripped = StripTags(markup);

        // Decode after stripping so that entity-escaped markup — &lt;p&gt;, which
        // plenty of feeds send — becomes visible text rather than a second round
        // of tags to strip. A feed that escapes its angle brackets meant them.
        var decoded = WebUtility.HtmlDecode(stripped);

        var collapsed = CollapseWhitespace(decoded);
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string StripTags(string markup)
    {
        var builder = new StringBuilder(markup.Length);
        var index = 0;

        while (index < markup.Length)
        {
            var open = markup.IndexOf('<', index);
            if (open < 0)
            {
                builder.Append(markup, index, markup.Length - index);
                break;
            }

            builder.Append(markup, index, open - index);

            var close = markup.IndexOf('>', open);
            if (close < 0)
            {
                // An unclosed tag at the end: everything before it is real prose and
                // is already appended, so stop rather than swallowing the remainder.
                break;
            }

            if (IsBreakingTag(markup.AsSpan(open + 1, close - open - 1)))
            {
                builder.Append('\n');
            }

            index = close + 1;
        }

        return builder.ToString();
    }

    private static bool IsBreakingTag(ReadOnlySpan<char> inner)
    {
        inner = inner.Trim();
        if (inner.Length > 0 && inner[0] == '/')
        {
            inner = inner[1..];
        }

        // Stop at the first delimiter so "p class=x" and "br/" both resolve.
        var end = inner.IndexOfAny([' ', '\t', '\n', '\r', '/']);
        var name = end < 0 ? inner : inner[..end];

        foreach (var tag in BreakingTags)
        {
            if (name.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingNewlines = 0;
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (character is '\n' or '\r')
            {
                pendingNewlines++;
                pendingSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }

            if (builder.Length > 0)
            {
                // A newline outranks a space, and any run of either collapses to one:
                // block tags nest, so "</p><br/><p>" is three breaks meaning one.
                if (pendingNewlines > 0)
                {
                    builder.Append('\n');
                }
                else if (pendingSpace)
                {
                    builder.Append(' ');
                }
            }

            pendingNewlines = 0;
            pendingSpace = false;
            builder.Append(character);
        }

        return builder.ToString();
    }
}
