namespace Folio.Domain.Markup;

/// <summary>Where one note ends up: the line it belongs to, and the top it is drawn at.</summary>
public readonly record struct PlacedNote(int AnchorLine, double AnchorY, double Top, double Height)
{
    public double Bottom => Top + Height;

    /// <summary>True when the note had to be pushed down, so a leader has to reach back for it.</summary>
    public bool NeedsLeader => Top > AnchorY + 0.001;
}

/// <summary>What a page's margin holds, and how much of the page's text fits beside it.</summary>
public sealed record PackedMargin(IReadOnlyList<PlacedNote> Notes, double TextHeight, int NotesDeferred);

/// <summary>
/// Packs the notes down the margin so none overlaps, and says where the page ends.
///
/// <para><b>Packing.</b> Each note wants to sit at its anchor line. When the note above has not
/// finished, it is pushed down until it clears, and a leader takes it back to the line it concerns
/// (F38). That is a layout pass, not a coordinate, which is why it is a function rather than a few
/// arithmetic expressions inside a renderer.</para>
///
/// <para><b>And the break, which is the owner's decision at the Release G gate.</b> When the packed
/// column runs past the foot of the page, the page ends <i>early</i>: the manuscript text stops at
/// whatever line still has its note on the same sheet. Nothing is ever separated from what it refers
/// to, and a leader never crosses a page boundary. The cost was accepted knowingly — a heavily
/// marked chapter prints ragged, and a dense run makes the markup longer than the ~140 pages F45
/// measured. The alternative, carrying a note over and reprinting its anchor line, keeps the
/// pagination tidy and was refused: a note beside the wrong page of prose is worse than a short
/// page.</para>
///
/// <para>So this returns <see cref="PackedMargin.TextHeight"/> as well as the notes, and the
/// paginator takes it as the page's content height rather than using the full column.</para>
/// </summary>
public static class MarginColumn
{
    /// <summary>Air between two stacked notes.</summary>
    public const double NoteGap = 8;

    /// <summary>
    /// Packs <paramref name="notes"/> — each an (anchor line, anchor Y, height) — into a column of
    /// <paramref name="columnHeight"/>, and reports how much of the page's text can be kept.
    /// </summary>
    public static PackedMargin Place(
        IReadOnlyList<(int Line, double AnchorY, double Height)> notes, double columnHeight)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var placed = new List<PlacedNote>();
        var cursor = 0.0;
        var deferred = 0;
        var textHeight = columnHeight;

        foreach (var note in notes.OrderBy(note => note.AnchorY).ThenBy(note => note.Line))
        {
            var top = Math.Max(note.AnchorY, cursor);

            // Past the foot: this note and everything below it belong to the next page, and the text
            // stops at the line this note was anchored to so the two stay together.
            if (top + note.Height > columnHeight)
            {
                textHeight = Math.Min(textHeight, note.AnchorY);
                deferred = notes.Count - placed.Count;
                break;
            }

            placed.Add(new PlacedNote(note.Line, note.AnchorY, top, note.Height));
            cursor = top + note.Height + NoteGap;
        }

        // A single note taller than the whole column would otherwise set the text height to its
        // anchor and make no progress — the page would be empty and the next page identical. It is
        // kept on this page instead, overflowing, because a page that never advances is worse than
        // a long note.
        if (placed.Count == 0 && notes.Count > 0)
        {
            var first = notes.OrderBy(note => note.AnchorY).First();
            return new PackedMargin(
                [new PlacedNote(first.Line, first.AnchorY, first.AnchorY, first.Height)],
                columnHeight,
                notes.Count - 1);
        }

        return new PackedMargin(placed, textHeight, deferred);
    }
}
