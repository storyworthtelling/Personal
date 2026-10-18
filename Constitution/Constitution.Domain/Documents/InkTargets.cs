namespace Constitution.Domain.Documents;

/// <summary>The lanes of one circle-a-word choice and the hairlines between them.</summary>
public sealed record CircleLaneSet(IReadOnlyList<InkTarget> Lanes, IReadOnlyList<InkRect> Dividers)
{
    /// <summary>
    /// Each lane's word, centred in its lane, and the hairline between neighbours. Handing these back
    /// from the set means a page cannot forget the words (the rating is also written) or draw the
    /// dividers anywhere but where the lanes meet.
    /// </summary>
    public IEnumerable<PrintedItem> PrintedItems()
    {
        foreach (var lane in Lanes)
        {
            yield return new PrintedItem($"{lane.Id}-word", PrintedRole.Label, lane.Bounds, lane.Meaning, lane.Id, PrintedAlign.Centre);
        }

        var group = Lanes[0].Group;
        for (var i = 0; i < Dividers.Count; i++)
        {
            yield return new PrintedItem($"{group}-divider-{i + 1}", PrintedRole.Rule, Dividers[i], null, group);
        }
    }
}

/// <summary>
/// Page 4's ink targets: the one source of every box a pen answers (D14-D18). A painter draws what
/// these return and the reader looks up the same instances; neither types a coordinate of its own, so
/// a tick can never be read from the wrong box.
/// </summary>
public static class InkTargets
{
    public const float LargeTickSize = 28f;
    public const float StandardTickSize = 18f;

    /// <summary>Standard rows are 26 apart: an 18-point box and exactly the 8 points of clear space.</summary>
    public const float TickRowPitch = 26f;

    public const float CircleLaneWidth = 84f;
    public const float CircleLaneWidthFourWords = 68f;
    public const float CircleLaneHeight = 32f;
    public const float NumberBoxWidth = 96f;
    public const float NumberBoxHeight = 40f;
    public const float NoBecauseHeight = 40f;
    public const float NotesMarginWidth = 91f;

    /// <summary>Every target keeps this much clear of anything else on the page.</summary>
    public const float MinimumClearance = 8f;

    public const float LabelHeight = 16f;
    public const float BoxWeight = 1f;
    public const float DividerWeight = 0.5f;

    public static InkTarget LargeTick(string id, float left, float top, string meaning)
    {
        InkTarget.Require(id, meaning, "large tick");
        return new InkTarget(id, InkTargetKind.LargeTick, new InkRect(left, top, LargeTickSize, LargeTickSize), meaning);
    }

    /// <summary>A column of standard ticks, one per meaning, 26 points apart, ids "prefix-1", "prefix-2"...</summary>
    public static IReadOnlyList<InkTarget> TickRows(string prefix, float left, float top, IReadOnlyList<string> meanings)
    {
        var rows = new List<InkTarget>();
        for (var i = 0; i < meanings.Count; i++)
        {
            var id = $"{prefix}-{i + 1}";
            InkTarget.Require(id, meanings[i], "tick");
            rows.Add(new InkTarget(
                id, InkTargetKind.StandardTick,
                new InkRect(left, top + i * TickRowPitch, StandardTickSize, StandardTickSize), meanings[i]));
        }

        return rows;
    }

    /// <summary>
    /// Two to four words to circle, side by side with a hairline between each. Four words are 68 wide
    /// so the row still fits the page; fewer are 84. Each lane's meaning is its word.
    /// </summary>
    public static CircleLaneSet CircleLanes(string group, float left, float top, IReadOnlyList<string> words)
    {
        if (words.Count is < 2 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(words), words.Count,
                $"A circle-a-word choice has two to four words; '{group}' was given {words.Count}.");
        }

        var width = words.Count == 4 ? CircleLaneWidthFourWords : CircleLaneWidth;
        var lanes = new List<InkTarget>();
        var dividers = new List<InkRect>();

        for (var i = 0; i < words.Count; i++)
        {
            var id = $"{group}-{i + 1}";
            InkTarget.Require(id, words[i], "lane");
            lanes.Add(new InkTarget(
                id, InkTargetKind.CircleLane,
                new InkRect(left + i * width, top, width, CircleLaneHeight), words[i], Group: group));

            if (i > 0)
            {
                dividers.Add(new InkRect(left + i * width, top, 0f, CircleLaneHeight));
            }
        }

        return new CircleLaneSet(lanes, dividers);
    }

    /// <summary>One open box for a written number; the unit is printed outside it, never in it.</summary>
    public static InkTarget NumberBox(string id, float left, float top, string meaning, string unit)
    {
        InkTarget.Require(id, meaning, "number box");
        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException($"The number box '{id}' needs its unit, printed outside it.", nameof(unit));
        }

        return new InkTarget(
            id, InkTargetKind.NumberBox, new InkRect(left, top, NumberBoxWidth, NumberBoxHeight), meaning, unit);
    }

    /// <summary>Where the unit is printed: beside the box, clear of it, centred on its height.</summary>
    public static InkRect UnitLabelBounds(InkTarget numberBox, float width) =>
        new(numberBox.Bounds.Right + MinimumClearance, numberBox.Bounds.CentreY - LabelHeight / 2f, width, LabelHeight);

    /// <summary>
    /// The shaded "No, because" field, level with the YES tick and at the other end of the row (D32).
    /// A written reason here denies, and wins over a tick on the same row (D17).
    /// </summary>
    public static InkTarget NoBecause(string id, InkTarget yes, float right, float width)
    {
        InkTarget.Require(id, "a written reason for saying no", "reason field");
        var left = right - width;
        if (left <= yes.Bounds.Right + MinimumClearance)
        {
            throw new ArgumentException(
                $"The reason field '{id}' would sit within {MinimumClearance} points of its YES box; it belongs opposite it.",
                nameof(width));
        }

        return new InkTarget(
            id, InkTargetKind.NoBecause,
            new InkRect(left, yes.Bounds.CentreY - NoBecauseHeight / 2f, width, NoBecauseHeight),
            "a written reason for saying no");
    }

    /// <summary>The 91-point ruled column down the right edge for the owner's notes.</summary>
    public static InkTarget NotesMargin(string id, float top, float bottom)
    {
        InkTarget.Require(id, "notes written in the margin", "notes margin");
        return new InkTarget(
            id, InkTargetKind.NotesMargin,
            new InkRect(ConstitutionPage.Width - ConstitutionPage.MarginRight - NotesMarginWidth, top, NotesMarginWidth, bottom - top),
            "notes written in the margin");
    }

    /// <summary>The ruled lines inside a notes margin, one per row pitch; they belong to the margin.</summary>
    public static IEnumerable<PrintedItem> NotesRules(InkTarget margin)
    {
        var count = 0;
        for (var y = margin.Bounds.Top + TickRowPitch; y < margin.Bounds.Bottom; y += TickRowPitch)
        {
            count++;
            yield return new PrintedItem(
                $"{margin.Id}-rule-{count}", PrintedRole.Rule, new InkRect(margin.Bounds.Left, y, margin.Bounds.Width, 0f), null, margin.Id);
        }
    }
}
