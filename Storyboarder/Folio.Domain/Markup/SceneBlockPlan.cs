namespace Folio.Domain.Markup;

/// <summary>
/// Where a scene's block goes in the marked manuscript: under the scene's last words.
///
/// <para>A scene's span usually runs on through the blank lines and the break before the next scene,
/// so the line its raw end offset falls on is white space, or — when that space reaches the next
/// scene's first line — the wrong scene. The end is walked back over whitespace to the last character
/// that is actually text, and the block follows the line holding it.</para>
/// </summary>
public static class SceneBlockPlan
{
    /// <summary>
    /// The index of the line a scene's block follows, or null when the end is unknown (no offset, or
    /// nothing but whitespace before it) — the caller then puts the block after the last line.
    /// </summary>
    public static int? LineAfter(string manuscript, IReadOnlyList<LineSpan> lines, int endOffset)
    {
        ArgumentNullException.ThrowIfNull(manuscript);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0 || endOffset <= 0)
        {
            return null;
        }

        var end = Math.Min(endOffset, manuscript.Length);
        while (end > 0 && char.IsWhiteSpace(manuscript[end - 1]))
        {
            end--;
        }

        if (end <= 0)
        {
            return null;
        }

        var last = end - 1;
        var line = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartOffset <= last)
            {
                line = i;
            }
            else
            {
                break;
            }
        }

        return line;
    }
}
