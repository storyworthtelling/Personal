namespace Watch.Domain.Dash;

/// <summary>
/// Dash's screens as the mockups place them (Watch/design/screens-r1.html: <c>head</c>, <c>row</c>, <c>bubble</c>,
/// <c>pill</c>, "Replying by voice"), in fractions of the round screen's radius: 0 is the top or left edge, 1 the
/// centre, 2 the bottom or right. Words are set at the three token sizes (WD §5), not at the mockups' fractions, so a
/// row or a pill grows to fit its words and keeps the drawn size as its least; a pill is always a whole touch target.
/// </summary>
public static class DashLayout
{
    // ---- the head every list screen wears: the time, then Dash's glyph ----

    /// <summary>The time's line, where Wear OS puts it.</summary>
    public const double TimeCentre = 0.13;

    public const double GlyphTop = 0.2;

    public const double GlyphSide = 0.18;

    // ---- People waiting's rows ----

    public const double RowLeft = 0.24;

    public const double RowWidth = 1.52;

    /// <summary>The row the mockup draws: its least height.</summary>
    public const double RowHeight = 0.3;

    /// <summary>From one row's centre to the next.</summary>
    public const double RowPitch = 0.36;

    /// <summary>The first row's centre.</summary>
    public const double FirstRow = 0.62;

    public const double RowCorner = 0.08;

    /// <summary>The words' inset from the row's edges.</summary>
    public const double RowPadding = 0.06;

    /// <summary>
    /// The space between a row's name and its line. No unread mark is drawn (owner's ruling, 27 Nov 2026: WD §1's "no
    /// unread marks" wins over the mockup's rule down a row's edge).
    /// </summary>
    public const double RowGap = 0.014;

    // ---- a conversation's bubbles ----

    /// <summary>The conversation's name, at the top.</summary>
    public const double TitleCentre = 0.3;

    public const double BubblesTop = 0.44;

    /// <summary>How far a bubble stands from its side of the screen: theirs from the left, the owner's from the right.</summary>
    public const double BubbleSide = 0.32;

    public const double BubbleWidth = 1.12;

    /// <summary>A one-line bubble, as drawn: its least height.</summary>
    public const double BubbleMinHeight = 0.19;

    public const double BubbleCorner = 0.07;

    public const double BubblePadding = 0.06;

    public const double BubbleGap = 0.05;

    // ---- the pinned pills, and the reply's quieter choices ----

    public const double PillHeight = 0.2;

    public const double ReplyWidth = 0.8;

    public const double ReplyToAllWidth = 0.9;

    public const double ReplyCentre = 1.62;

    /// <summary>"To Lee", over the words read back.</summary>
    public const double ToCentre = 0.32;

    /// <summary>The first line of the words read back.</summary>
    public const double WordsTop = 0.62;

    public const double WordsWidth = 1.3;

    public const double SendWidth = 0.7;

    public const double SendCentre = 1.32;

    /// <summary>Keyboard and Again, either side of the centre.</summary>
    public const double SecondaryCentre = 1.62;

    public const double SecondaryOffset = 0.34;
}
