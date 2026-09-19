namespace Folio.Domain.Markup;

/// <summary>One laid-out line of the manuscript, as a half-open span of the stored text.</summary>
public readonly record struct LineSpan(int Index, int StartOffset, int EndOffset);

/// <summary>
/// Which lines a mark falls on.
///
/// <para>This is the whole of finding R-6 in one function. Marks resolve to character offsets into
/// the stored manuscript, and if the text and the offsets ever disagree every mark lands somewhere
/// else — <b>silently</b>, because a misplaced strike-through is still a strike-through and the page
/// still prints. Nothing downstream can tell a correct mark from one a character out.</para>
///
/// <para>So the interval arithmetic is here, pure, and tested without rendering anything. Both ends
/// are half-open: a run ending exactly where a line begins does <b>not</b> touch that line, which is
/// the off-by-one the guard reintroduces.</para>
/// </summary>
public static class MarkPlacement
{
    /// <summary>The lines this mark touches, in order. Empty when it falls outside the page.</summary>
    public static IReadOnlyList<int> Lines(Mark mark, IReadOnlyList<LineSpan> lines)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ArgumentNullException.ThrowIfNull(lines);

        // A caret marks a point between two characters, so it belongs to the line containing that
        // point — an empty span would otherwise overlap nothing and the caret would never draw.
        if (mark.IsPoint)
        {
            var line = lines.FirstOrDefault(
                span => mark.StartOffset >= span.StartOffset && mark.StartOffset < span.EndOffset);
            return line == default && !Contains(lines, mark.StartOffset) ? [] : [line.Index];
        }

        return lines
            .Where(span => span.StartOffset < mark.EndOffset && mark.StartOffset < span.EndOffset)
            .Select(span => span.Index)
            .ToList();
    }

    /// <summary>
    /// The line a mark's note is anchored to: the first it touches. The note is packed down from
    /// here when it collides with the one above, and a leader takes it back.
    /// </summary>
    public static int? AnchorLine(Mark mark, IReadOnlyList<LineSpan> lines)
    {
        var touched = Lines(mark, lines);
        return touched.Count == 0 ? null : touched[0];
    }

    /// <summary>
    /// The part of one line a mark actually covers, or null when it covers none of it.
    ///
    /// <para>A mark is drawn once per line it touches, not once per mark: a dele over a sentence that
    /// wraps has to strike both halves, and the first render of the marked page struck only the line
    /// the note was anchored to — so half a struck sentence printed as if it were staying.</para>
    /// </summary>
    public static TextRange? RunOn(Mark mark, LineSpan line)
    {
        ArgumentNullException.ThrowIfNull(mark);

        if (mark.IsPoint)
        {
            return mark.StartOffset >= line.StartOffset && mark.StartOffset < line.EndOffset
                ? new TextRange(mark.StartOffset, mark.StartOffset)
                : null;
        }

        var from = Math.Max(mark.StartOffset, line.StartOffset);
        var to = Math.Min(mark.EndOffset, line.EndOffset);
        return to > from ? new TextRange(from, to) : null;
    }

    private static bool Contains(IReadOnlyList<LineSpan> lines, int offset) =>
        lines.Any(span => offset >= span.StartOffset && offset < span.EndOffset);
}
