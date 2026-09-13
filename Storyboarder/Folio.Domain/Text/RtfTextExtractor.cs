using System.Text;

namespace Folio.Domain.Text;

// Extracts the plain text of an RTF document. RTF is control-word markup, so this is a small pure-C#
// parser (no dependency): it walks the groups, drops the header/metadata destinations (font and colour
// tables, stylesheet, info), turns paragraph/line/tab controls into whitespace, decodes \uN and \'hh
// character escapes, and emits everything else as text. Deliberately lenient — a control word it doesn't
// recognize is simply ignored — so a slightly non-standard file still yields readable prose.
public static class RtfTextExtractor
{
    // Destinations whose contents are not body text and are skipped whole.
    private static readonly HashSet<string> IgnoredDestinations = new(StringComparer.Ordinal)
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "themedata", "colorschememapping",
        "latentstyles", "datastore", "generator", "listtable", "listoverridetable", "rsidtbl", "mmath",
        "wgrffmtfilter", "xmlnstbl", "fldinst", "header", "footer", "headerl", "headerr", "footerl", "footerr",
    };

    public static string Extract(string rtf)
    {
        if (string.IsNullOrEmpty(rtf))
            return string.Empty;

        var output = new StringBuilder(rtf.Length / 2);
        var ignoreDepth = new Stack<bool>(); // whether the current group's text is skipped
        var skippingToDepth = -1;            // brace depth we are skipping a destination down to (or -1)
        var depth = 0;
        var unicodeSkip = 1;                 // \ucN — bytes to skip after a \uN
        var i = 0;

        void Emit(char c)
        {
            if (skippingToDepth < 0)
                output.Append(c);
        }

        while (i < rtf.Length)
        {
            var c = rtf[i];

            if (c == '{')
            {
                depth++;
                ignoreDepth.Push(false);
                i++;
            }
            else if (c == '}')
            {
                if (skippingToDepth >= 0 && depth <= skippingToDepth)
                    skippingToDepth = -1;
                depth--;
                if (ignoreDepth.Count > 0)
                    ignoreDepth.Pop();
                i++;
            }
            else if (c == '\\')
            {
                // An escaped literal, a hex byte, or a control word.
                if (i + 1 < rtf.Length && (rtf[i + 1] == '\\' || rtf[i + 1] == '{' || rtf[i + 1] == '}'))
                {
                    Emit(rtf[i + 1]);
                    i += 2;
                }
                else if (i + 1 < rtf.Length && rtf[i + 1] == '\'')
                {
                    // \'hh — a byte in the current code page; decode as Latin-1 (good enough for prose).
                    if (i + 3 < rtf.Length && Uri.IsHexDigit(rtf[i + 2]) && Uri.IsHexDigit(rtf[i + 3]))
                    {
                        var value = Convert.ToInt32(rtf.Substring(i + 2, 2), 16);
                        Emit((char)value);
                        i += 4;
                    }
                    else
                    {
                        i += 2;
                    }
                }
                else if (i + 1 < rtf.Length && rtf[i + 1] == '*')
                {
                    // \* — an optional destination; skip the whole group unless it's plainly body text.
                    if (skippingToDepth < 0)
                        skippingToDepth = depth;
                    i += 2;
                }
                else
                {
                    i = ReadControlWord(rtf, i, output, ref skippingToDepth, ref unicodeSkip, depth, Emit);
                }
            }
            else if (c is '\r' or '\n')
            {
                // Raw line breaks in the RTF source are not content.
                i++;
            }
            else
            {
                Emit(c);
                i++;
            }
        }

        return Normalize(output.ToString());
    }

    private static int ReadControlWord(
        string rtf, int start, StringBuilder output, ref int skippingToDepth, ref int unicodeSkip, int depth,
        Action<char> emit)
    {
        var i = start + 1; // past the backslash
        var wordStart = i;
        while (i < rtf.Length && char.IsLetter(rtf[i]))
            i++;
        var word = rtf[wordStart..i];

        // Optional numeric parameter (possibly negative).
        var negative = false;
        if (i < rtf.Length && rtf[i] == '-')
        {
            negative = true;
            i++;
        }
        var numStart = i;
        while (i < rtf.Length && char.IsDigit(rtf[i]))
            i++;
        int? parameter = i > numStart ? int.Parse(rtf[numStart..i]) * (negative ? -1 : 1) : null;

        // A single trailing space is part of the control word (a delimiter), not content.
        if (i < rtf.Length && rtf[i] == ' ')
            i++;

        switch (word)
        {
            case "par":
            case "line":
                emit('\n');
                break;
            case "tab":
                emit('\t');
                break;
            case "uc":
                unicodeSkip = parameter ?? 1;
                break;
            case "u":
                if (parameter is { } code)
                {
                    emit((char)(code < 0 ? code + 0x10000 : code));
                    // Skip the following fallback characters that stand in for the Unicode char.
                    var skip = unicodeSkip;
                    while (skip-- > 0 && i < rtf.Length && rtf[i] != '\\' && rtf[i] != '{' && rtf[i] != '}')
                        i++;
                }
                break;
            default:
                if (IgnoredDestinations.Contains(word) && skippingToDepth < 0)
                    skippingToDepth = depth;
                break;
        }

        return i;
    }

    // Collapse the runs of stray whitespace RTF produces into clean paragraphs.
    private static string Normalize(string text)
    {
        var lines = text.Replace("\r", string.Empty).Split('\n');
        var paragraphs = lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);
        return string.Join("\n\n", paragraphs);
    }
}
