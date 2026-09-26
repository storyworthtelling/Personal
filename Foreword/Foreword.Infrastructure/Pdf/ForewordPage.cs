using SkiaSharp;
using Foreword.Domain.Brief;
using Foreword.Domain.Time;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// The brief's first page (B1): a near-empty page, by design.
///
/// <para>The name in Serif <b>Black</b> over the identity rule — not the calligraphy hand, which §4
/// reserves for the owner's voice and which here belongs only to the composed paragraph. Then the
/// date written out, the voiced opening, the maker's mark, and the contents at the foot so the brief
/// says what it holds and how long it runs before a page is turned.</para>
///
/// <para>The emptiness is the point. It is the page that says this was set and delivered for one
/// person, and filling it would make it a cover sheet instead.</para>
/// </summary>
internal static class ForewordPage
{
    private const double MarkSize = 96;
    private const double RuleHeight = 2;

    private static readonly SKColor Identity = AppIdentityTokens.ForewordSunrise;

    public static void Draw(
        SkiaPageLayout layout, DateOnly date, string voice, IReadOnlyList<BriefSection> sections,
        HistoryNote? history = null)
    {
        var kickerFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var nameFont = SkiaPageLayout.Serif(PaperTypeScale.Display, black: true);
        var dateFont = SkiaPageLayout.Serif(PaperTypeScale.Heading);
        var voiceFont = SkiaPageLayout.Script(PaperTypeScale.Title);

        layout.MoveTo(SkiaPageLayout.Margin * 1.5);

        layout.DrawText("THE MORNING BRIEF", kickerFont, SkiaPageLayout.Margin, layout.Y,
            layout.ContentWidth, 12, TextAlign.TopCenter, SkiaPageLayout.InkMuted);
        layout.Advance(16);

        layout.DrawCentered("Foreword", nameFont, SkiaPageLayout.LineHeight(nameFont));

        layout.DrawCentered(WrittenOut(date), dateFont, SkiaPageLayout.LineHeight(dateFont) + 4);

        layout.Advance(16);
        layout.DrawRect(
            SkiaPageLayout.Margin + layout.ContentWidth / 3,
            layout.Y,
            layout.ContentWidth / 3,
            RuleHeight,
            fill: Identity);
        layout.Advance(24);

        // The owner's own voice, and the only thing on this page set in their hand.
        if (!string.IsNullOrWhiteSpace(voice))
        {
            foreach (var line in SkiaText.Wrap(voiceFont, voice, (float)layout.ContentWidth))
            {
                layout.DrawCentered(line, voiceFont, SkiaPageLayout.LineHeight(voiceFont, 1.35));
            }
        }

        DrawHistory(layout, history);
        DrawContents(layout, sections);
        DrawMakersMark(layout, sections.Count > 0);
    }

    /// <summary>
    /// "Sunday, the sixth of September" — written out, because this is the one page in the document
    /// with room to say a date rather than stamp one.
    /// </summary>
    internal static string WrittenOut(DateOnly date) =>
        $"{date.DayOfWeek}, the {Ordinal(date.Day)} of "
        + date.ToDateTime(TimeOnly.MinValue).ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture);

    private static string Ordinal(int day) => day switch
    {
        1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth",
        6 => "sixth", 7 => "seventh", 8 => "eighth", 9 => "ninth", 10 => "tenth",
        11 => "eleventh", 12 => "twelfth", 13 => "thirteenth", 14 => "fourteenth",
        15 => "fifteenth", 16 => "sixteenth", 17 => "seventeenth", 18 => "eighteenth",
        19 => "nineteenth", 20 => "twentieth", 21 => "twenty-first", 22 => "twenty-second",
        23 => "twenty-third", 24 => "twenty-fourth", 25 => "twenty-fifth",
        26 => "twenty-sixth", 27 => "twenty-seventh", 28 => "twenty-eighth",
        29 => "twenty-ninth", 30 => "thirtieth", _ => "thirty-first",
    };

    /// <summary>
    /// One thing that happened on this date, under the voice.
    ///
    /// <para>Set apart and quiet: it is the one line on the document that is neither the owner's
    /// day nor their reading, and it earns its place by being short. Nothing is drawn when nothing
    /// qualified — an empty heading over a blank line is worse than no heading.</para>
    /// </summary>
    private static void DrawHistory(SkiaPageLayout layout, HistoryNote? history)
    {
        if (history is null)
        {
            return;
        }

        var yearFont = SkiaPageLayout.Serif(PaperTypeScale.Heading);
        var textFont = SkiaPageLayout.Serif(PaperTypeScale.Body);

        layout.Advance(24);
        layout.DrawCentered(history.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            yearFont, SkiaPageLayout.LineHeight(yearFont) + 2);

        // Held to a narrower measure than the page, so it reads as an aside rather than as a
        // paragraph of the brief.
        var measure = layout.ContentWidth * 0.72;
        var left = SkiaPageLayout.Margin + (layout.ContentWidth - measure) / 2;

        foreach (var line in SkiaText.Wrap(textFont, history.Text, (float)measure))
        {
            layout.DrawText(line, textFont, left, layout.Y, measure,
                SkiaPageLayout.LineHeight(textFont, 1.4), TextAlign.TopCenter,
                SkiaPageLayout.InkSoft);
            layout.Advance(SkiaPageLayout.LineHeight(textFont, 1.4));
        }
    }

    /// <summary>
    /// The contents, at the foot. Measured from the bottom rather than flowed from the voice, so a
    /// long opening paragraph cannot push it off the page — it is the half of this sheet with a job.
    /// </summary>
    private static void DrawContents(SkiaPageLayout layout, IReadOnlyList<BriefSection> sections)
    {
        var rows = BriefContents.Rows(sections);
        if (rows.Count == 0)
        {
            return;
        }

        var nameFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary, bold: true);
        var detailFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary);
        var lineHeight = SkiaPageLayout.LineHeight(nameFont) + 4;

        var top = layout.BottomY - MarkSize - 24 - rows.Count * lineHeight;

        foreach (var row in rows)
        {
            layout.DrawText(row.Name, nameFont, SkiaPageLayout.Margin, top,
                layout.ContentWidth, lineHeight, TextAlign.TopLeft);
            layout.DrawText(row.Detail, detailFont, SkiaPageLayout.Margin, top,
                layout.ContentWidth, lineHeight, TextAlign.TopRight, SkiaPageLayout.InkMuted);

            top += lineHeight;
            layout.DrawLine(SkiaPageLayout.Margin, top - 2,
                SkiaPageLayout.Margin + layout.ContentWidth, top - 2, SkiaPageLayout.Hairline, 0.5f);
        }
    }

    /// <summary>
    /// The maker's mark (§13): the owner's drawn mark and the line that says who set the paper.
    ///
    /// <para>The same mark Folio prints on a report cover, not a photograph. There it takes the
    /// jacket's colourway; the brief has no jacket, so it takes the page's own ink on its own paper —
    /// which is exactly what Folio does for a cover that has none.</para>
    /// </summary>
    private static void DrawMakersMark(SkiaPageLayout layout, bool hasContents)
    {
        var markFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var bottom = layout.BottomY;

        var left = SkiaPageLayout.Margin + (layout.ContentWidth - MarkSize) / 2;
        var top = bottom - MarkSize - 14;
        layout.DrawOwnersMark(left, top, MarkSize,
            SkiaPageLayout.Ground, SkiaPageLayout.Ink, SkiaPageLayout.Ink);

        layout.DrawText("SET AND DELIVERED BY FOREWORD", markFont,
            SkiaPageLayout.Margin, bottom - 12, layout.ContentWidth, 12,
            TextAlign.TopCenter, SkiaPageLayout.InkMuted);

        _ = hasContents;
    }
}
