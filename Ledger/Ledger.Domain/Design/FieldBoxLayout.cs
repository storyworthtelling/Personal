namespace Index.Domain.Design;

/// <summary>How wide a <c>FieldBox</c> column is: sized to its content, or taking what is left.</summary>
public enum FieldBoxColumn
{
    Auto,
    Star,
}

/// <summary>
/// The columns inside a <c>FieldBox</c> (DESIGN.md §7), decided as data so the view only builds what this says.
/// The entry takes every column that is not an affix's, and a box with no affix is a single column: no empty
/// column, no spacing, so the text sits exactly the box's inner padding in. A prefix ("$") gets an Auto column
/// before the entry and a suffix ("%") one after it, and the entry never spans under either. The gap between an
/// affix and the entry is the affix's own margin (<see cref="AffixGapDp"/>), not a column spacing that would
/// also be paid by boxes without one.
/// </summary>
public sealed record FieldBoxLayout(
    IReadOnlyList<FieldBoxColumn> Columns, int? PrefixColumn, int EntryColumn, int? SuffixColumn)
{
    /// <summary>Space between an affix and the entry, in dp.</summary>
    public const int AffixGapDp = 6;

    /// <summary>The entry fills exactly one column: the one between (or beside) the affixes.</summary>
    public int EntryColumnSpan => 1;

    public static FieldBoxLayout For(bool hasPrefix, bool hasSuffix)
    {
        var columns = new List<FieldBoxColumn>();
        int? prefixColumn = null;
        int? suffixColumn = null;

        if (hasPrefix)
        {
            prefixColumn = columns.Count;
            columns.Add(FieldBoxColumn.Auto);
        }

        var entryColumn = columns.Count;
        columns.Add(FieldBoxColumn.Star);

        if (hasSuffix)
        {
            suffixColumn = columns.Count;
            columns.Add(FieldBoxColumn.Auto);
        }

        return new FieldBoxLayout(columns, prefixColumn, entryColumn, suffixColumn);
    }
}
