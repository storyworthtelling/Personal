using Index.Domain.Design;
using Xunit;

namespace Index.Tests.Design;

/// <summary>
/// <c>FieldBox</c>'s columns, decided as data: the entry takes the whole inside of the box unless a prefix
/// ("$") or a suffix ("%") needs a column of its own. The view used to give every box three columns and six
/// points of spacing, so a plain box sat its text 18dp in rather than 12, and a percent box's entry ran
/// underneath its "%".
/// </summary>
public class FieldBoxLayoutTests
{
    [Fact]
    public void APlainBoxIsOneStarColumnAndTheEntryFillsIt()
    {
        var layout = FieldBoxLayout.For(hasPrefix: false, hasSuffix: false);

        Assert.Equal([FieldBoxColumn.Star], layout.Columns);
        Assert.Equal(0, layout.EntryColumn);
        Assert.Null(layout.PrefixColumn);
        Assert.Null(layout.SuffixColumn);
    }

    [Fact]
    public void APrefixBoxIsAnAutoColumnThenTheEntry()
    {
        var layout = FieldBoxLayout.For(hasPrefix: true, hasSuffix: false);

        Assert.Equal([FieldBoxColumn.Auto, FieldBoxColumn.Star], layout.Columns);
        Assert.Equal(0, layout.PrefixColumn);
        Assert.Equal(1, layout.EntryColumn);
        Assert.Null(layout.SuffixColumn);
    }

    [Fact]
    public void ASuffixBoxEndsInAnAutoColumnAndTheEntryStopsBeforeIt()
    {
        var layout = FieldBoxLayout.For(hasPrefix: false, hasSuffix: true);

        Assert.Equal([FieldBoxColumn.Star, FieldBoxColumn.Auto], layout.Columns);
        Assert.Equal(0, layout.EntryColumn);
        Assert.Equal(1, layout.SuffixColumn);
        Assert.Null(layout.PrefixColumn);
        Assert.Equal(1, layout.EntryColumnSpan);
    }

    [Fact]
    public void ABoxWithBothHasThreeColumnsWithTheEntryBetween()
    {
        var layout = FieldBoxLayout.For(hasPrefix: true, hasSuffix: true);

        Assert.Equal([FieldBoxColumn.Auto, FieldBoxColumn.Star, FieldBoxColumn.Auto], layout.Columns);
        Assert.Equal(0, layout.PrefixColumn);
        Assert.Equal(1, layout.EntryColumn);
        Assert.Equal(2, layout.SuffixColumn);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NoEntryEverSitsUnderAnAffixAndNoColumnIsLeftEmpty(bool hasPrefix, bool hasSuffix)
    {
        var layout = FieldBoxLayout.For(hasPrefix, hasSuffix);

        var occupied = new List<int> { layout.EntryColumn };
        if (layout.PrefixColumn is { } prefixColumn)
        {
            occupied.Add(prefixColumn);
        }
        if (layout.SuffixColumn is { } suffixColumn)
        {
            occupied.Add(suffixColumn);
        }

        Assert.Equal(Enumerable.Range(0, layout.Columns.Count), occupied.Order());
        Assert.Equal(1, layout.EntryColumnSpan);
        Assert.Equal(FieldBoxColumn.Star, layout.Columns[layout.EntryColumn]);
    }

    [Fact]
    public void TheViewTakesItsColumnsFromTheLayoutAndLeavesNoSpacingBetweenThem()
    {
        var source = File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android/Views/FieldBox.cs"));

        Assert.Contains("FieldBoxLayout.For(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.SetColumnSpan(entry, 2)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnSpacing = 6", source, StringComparison.Ordinal);
    }
}
