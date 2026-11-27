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

    // ---- B5: a group's photo (mock: A group text with a photo) ----

    public const double PhotoWidth = 1.0;

    public const double PhotoHeight = 0.6;

    public const double PhotoCorner = 0.06;

    /// <summary>Where the mockup draws its one photo's top.</summary>
    public const double PhotoTop = 0.54;

    /// <summary>From a photo's foot to its caption's centre ("Robin · 1 h").</summary>
    public const double CaptionCentre = 0.1;

    /// <summary>A group's Reply to all, under the photo's caption.</summary>
    public const double ReplyToAllCentre = 1.52;

    // ---- B5: the Undo ring (mock: Filed, with Undo) ----

    public const double UndoGlyphCentre = 0.62;

    /// <summary>"Lee filed".</summary>
    public const double UndoTitleCentre = 1.18;

    /// <summary>"Tap to undo".</summary>
    public const double UndoPromptCentre = 1.4;

    // ---- B5: the choice before an act, on After the call's layout (no mockup draws it) ----

    public const double ActTitleCentre = 0.66;

    public const double ActPillCentre = 1.18;

    public const double ActPillWidth = 1.0;

    /// <summary>The quieter choice under the pill ("Delete"), where After the call puts "Not now".</summary>
    public const double ActChoiceCentre = 1.48;

    // ---- B6: New text, at the foot of People waiting where the mockup draws Mail ----

    /// <summary>
    /// The gap above New text: it follows the last row as the rows follow each other, so a row's height of it is centred a
    /// row's pitch under the last row, where the mockup draws Mail (no mockup draws New text).
    /// </summary>
    public const double EntryGap = RowPitch - RowHeight;
}
