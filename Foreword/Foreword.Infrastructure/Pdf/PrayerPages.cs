using SkiaSharp;
using Foreword.Domain.Document;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Prayer;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// The prayer document (P1, P2): the list and what it surfaces back, then the journalling space.
///
/// <para>It carries the same frame as the working documents — header, identity rule, binder tabs —
/// deliberately, so it reads as part of the same morning rather than as a different app's output.
/// What differs is that one of its two pages has nothing on it at all.</para>
///
/// <para>This is one of the four declared <see cref="IPrayerReader"/>s. Printing needs the text, so
/// the permission is declared here rather than assumed at the call site.</para>
/// </summary>
public sealed class PrayerPages : IPrayerPdfComposer, IPrayerReader
{
    private const double LeadGap = 14;
    private const double SectionGap = 22;
    private const double HeadingRule = 4;
    private const double RowHeight = 30;
    private const double PastRowGap = 7;

    public byte[] Compose(PrayerDocument document)
    {
        using var layout = new SkiaPageLayout();

        layout.Footer = Folio("Prayer · the list", 1);
        DrawList(layout, document);

        layout.NewPage();

        layout.Footer = Folio("Prayer · journal", 2);
        DrawJournal(layout);

        return layout.ToPdf();
    }

    private void DrawList(SkiaPageLayout layout, PrayerDocument document)
    {
        PageFurniture.DrawHeader(layout, DayTitles.PageHeading(document.Date));
        DrawTabs(layout, PrayerSection.List);

        DrawLead(layout, document.Lead);
        DrawHolding(layout, document.Holding);
        DrawRotations(layout, document.Rotations);
        DrawFromBefore(layout, document.FromBefore);
    }

    /// <summary>
    /// The pastoral line, in the owner's hand. Nothing is drawn when the call did not come back —
    /// the page simply has no line, rather than a neutral sentence pretending to be one.
    /// </summary>
    private void DrawLead(SkiaPageLayout layout, PrayerText lead)
    {
        if (lead.IsEmpty)
        {
            return;
        }

        var font = SkiaPageLayout.Script(PaperTypeScale.Title);

        layout.Advance(LeadGap);

        foreach (var line in SkiaText.Wrap(font, lead.ReadBy(this), (float)PageFurniture.ContentWidth(layout)))
        {
            layout.DrawText(line, font, SkiaPageLayout.Margin, layout.Y,
                PageFurniture.ContentWidth(layout), SkiaPageLayout.LineHeight(font, 1.3),
                TextAlign.TopLeft);
            layout.Advance(SkiaPageLayout.LineHeight(font, 1.3));
        }
    }

    private void DrawHolding(SkiaPageLayout layout, IReadOnlyList<PrintedRequest> holding)
    {
        if (holding.Count == 0)
        {
            return;
        }

        DrawSectionHeading(layout, "Holding");

        var nameFont = SkiaPageLayout.Serif(PaperTypeScale.Heading);
        var sinceFont = SkiaPageLayout.Sans(PaperTypeScale.Label);
        var width = PageFurniture.ContentWidth(layout);

        foreach (var request in holding)
        {
            var text = request.Text.ReadBy(this);
            var ink = request.Struck ? SkiaPageLayout.InkMuted : SkiaPageLayout.Ink;

            // Both sit on one baseline inside the row, so the name and its held-for line read as
            // one row rather than as two lines that happen to be adjacent.
            var nameHeight = SkiaPageLayout.LineHeight(nameFont);
            var baseline = layout.Y + (RowHeight - nameHeight) / 2;

            layout.DrawText(text, nameFont, SkiaPageLayout.Margin, baseline, width, nameHeight,
                TextAlign.TopLeft, ink);
            layout.DrawText(request.Since, sinceFont, SkiaPageLayout.Margin, baseline, width,
                nameHeight, TextAlign.TopRight, SkiaPageLayout.InkMuted);

            // Striking it through is how it is answered, so the strike is the acknowledgement —
            // drawn over the words rather than beside them.
            if (request.Struck)
            {
                var textWidth = layout.MeasureText(text, nameFont);
                var middle = layout.Y + RowHeight / 2;
                layout.DrawLine(SkiaPageLayout.Margin, middle,
                    SkiaPageLayout.Margin + textWidth, middle, SkiaPageLayout.InkMuted, 1f);
            }

            layout.Advance(RowHeight);
            layout.DrawLine(SkiaPageLayout.Margin, layout.Y,
                SkiaPageLayout.Margin + width, layout.Y, SkiaPageLayout.Hairline, 0.5f);
        }
    }

    /// <summary>
    /// This morning's member of each standing rotation, each list under its own name.
    ///
    /// <para>Its own section, with no held-for line — the name was next, not carried. What it
    /// carries instead is its place in the list, so the page says how far round it has come.</para>
    /// </summary>
    private void DrawRotations(SkiaPageLayout layout, IReadOnlyList<RotationPicked> picks)
    {
        if (picks.Count == 0)
        {
            return;
        }

        var nameFont = SkiaPageLayout.Serif(PaperTypeScale.Heading);
        var positionFont = SkiaPageLayout.Sans(PaperTypeScale.Label);
        var width = PageFurniture.ContentWidth(layout);

        foreach (var pick in picks)
        {
            DrawSectionHeading(layout, pick.Rotation);

            var nameHeight = SkiaPageLayout.LineHeight(nameFont);
            var baseline = layout.Y + (RowHeight - nameHeight) / 2;

            layout.DrawText(pick.Member.ReadBy(this), nameFont, SkiaPageLayout.Margin, baseline,
                width, nameHeight, TextAlign.TopLeft);
            layout.DrawText(pick.Position, positionFont, SkiaPageLayout.Margin, baseline, width,
                nameHeight, TextAlign.TopRight, SkiaPageLayout.InkMuted);

            layout.Advance(RowHeight);
        }
    }

    /// <summary>
    /// What came before, surfaced <b>purely by date</b>.
    ///
    /// <para>The mockup set these in Serif Italic; the owner chose Serif Regular instead, so italic
    /// keeps the one role DESIGN.md gives it. Which means the quotation marks and the muted ink are
    /// the whole signal that these are words from another morning — so both are load-bearing here
    /// rather than decorative.</para>
    /// </summary>
    private void DrawFromBefore(SkiaPageLayout layout, IReadOnlyList<SurfacedEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        layout.Advance(SectionGap - HeadingRule);
        DrawSectionHeading(layout, "From before");

        var whenFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var textFont = SkiaPageLayout.Serif(PaperTypeScale.Body);
        var width = PageFurniture.ContentWidth(layout);
        var lineHeight = SkiaPageLayout.LineHeight(textFont, 1.45);

        foreach (var entry in entries)
        {
            layout.Advance(PastRowGap);

            layout.DrawText(entry.When, whenFont, SkiaPageLayout.Margin, layout.Y, width,
                SkiaPageLayout.LineHeight(whenFont), TextAlign.TopLeft, SkiaPageLayout.InkMuted);
            layout.Advance(SkiaPageLayout.LineHeight(whenFont) + 2);

            var quoted = $"“{entry.Text.ReadBy(this)}”";
            foreach (var line in SkiaText.Wrap(textFont, quoted, (float)width))
            {
                layout.DrawText(line, textFont, SkiaPageLayout.Margin, layout.Y, width, lineHeight,
                    TextAlign.TopLeft, SkiaPageLayout.InkSoft);
                layout.Advance(lineHeight);
            }

            layout.Advance(PastRowGap);
            layout.DrawLine(SkiaPageLayout.Margin, layout.Y,
                SkiaPageLayout.Margin + width, layout.Y, SkiaPageLayout.Hairline, 0.5f);
        }
    }

    /// <summary>
    /// Ruled space, and one labelled strip at the foot.
    ///
    /// <para>The free space is nearly the whole page and carries no heading, because a heading would
    /// make it a form and what is wanted is an invitation — which is what an empty ruled page
    /// already is. The strip at the foot is the other half of the owner's answer about how something
    /// gets onto the list: a one-off request is <b>written</b>, here, on the page they already have
    /// a pen on, and the standing rotations are edited on the phone.</para>
    ///
    /// <para>Both bands are painted from <see cref="JournalRegions"/>, which is the same source the
    /// transcription is told them from. That agreement is load-bearing: a line under the strip
    /// becomes a printed request, and the same line two inches higher stays private.</para>
    /// </summary>
    private void DrawJournal(SkiaPageLayout layout)
    {
        PageFurniture.DrawHeader(layout, PrayerTabs.Label(PrayerSection.Journal));
        DrawTabs(layout, PrayerSection.Journal);

        var regions = JournalRegions.For((float)layout.PageWidth, (float)layout.PageHeight);
        var free = regions[0];
        var adding = regions[1];

        DrawRules(layout, free.Left, free.Top, free.Width, free.Top + free.Height);

        // The strip's own heading, so there is no doubt which side of the line a pen is on.
        var headingFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);

        layout.DrawLine(adding.Left, adding.Top, adding.Left + adding.Width, adding.Top,
            SkiaPageLayout.Hairline, 1f);
        layout.DrawText("Add to the list", headingFont, adding.Left, adding.Top + HeadingRule,
            adding.Width, SkiaPageLayout.LineHeight(headingFont), TextAlign.TopLeft,
            SkiaPageLayout.InkSoft);

        var firstAddLine = adding.Top + HeadingRule + SkiaPageLayout.LineHeight(headingFont)
            + JournalRegions.RuleSpacing;

        DrawRules(layout, adding.Left, firstAddLine, adding.Width,
            adding.Top + adding.Height);
    }

    private static void DrawRules(
        SkiaPageLayout layout, double left, double from, double width, double until)
    {
        for (var y = from; y <= until; y += JournalRegions.RuleSpacing)
        {
            layout.DrawLine(left, y, left + width, y, SkiaPageLayout.Hairline, 0.5f);
        }
    }

    private static void DrawSectionHeading(SkiaPageLayout layout, string heading)
    {
        var font = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var width = PageFurniture.ContentWidth(layout);

        layout.Advance(SectionGap);
        layout.DrawText(heading, font, SkiaPageLayout.Margin, layout.Y, width,
            SkiaPageLayout.LineHeight(font), TextAlign.TopLeft, SkiaPageLayout.InkSoft);
        layout.Advance(SkiaPageLayout.LineHeight(font) + HeadingRule);

        layout.DrawLine(SkiaPageLayout.Margin, layout.Y, SkiaPageLayout.Margin + width, layout.Y,
            SkiaPageLayout.Hairline, 1f);
    }

    /// <summary>
    /// Both tabs on both pages, each page anchored, so the two are a tap apart on the tablet — the
    /// same grammar as The Day's four, through the same arithmetic.
    /// </summary>
    private static void DrawTabs(SkiaPageLayout layout, PrayerSection active)
    {
        layout.Anchor(PrayerTabs.Destination(active));

        PageFurniture.DrawBinderEdge(layout, SectionTabs.BinderEdgeInset, SectionTabs.Hairline);

        foreach (var tab in PrayerTabs.Layout((float)layout.PageWidth, active))
        {
            PageFurniture.DrawTab(
                layout, tab.Left, tab.Top, tab.Width, tab.Height, tab.Active,
                PrayerTabs.Label(tab.Section),
                PrayerTabs.Destination(tab.Section),
                Cloth(tab.Section),
                Ink(tab.Section),
                SectionTabs.Radius,
                SectionTabs.Hairline);
        }
    }

    /// <summary>
    /// The list takes the identity, because it is the document's own page; the journal takes the
    /// neutral resolved cloth, the same grey every app's Settings takes (§9) — it is a surface to
    /// write on rather than a kind of content.
    /// </summary>
    private static SKColor Cloth(PrayerSection section) => section switch
    {
        PrayerSection.List => AppIdentityTokens.ForewordGround,
        _ => PaperPalette.ResolveRole(ColorRole.Resolved, ColorTier.Fill, UiColorProfile.DocumentColor),
    };

    private static SKColor Ink(PrayerSection section) => section switch
    {
        PrayerSection.List => AppIdentityTokens.ForewordDeep,
        _ => PaperPalette.ResolveRole(ColorRole.Resolved, ColorTier.Text, UiColorProfile.DocumentColor),
    };

    private static Action<SkiaPageLayout> Folio(string name, int number) => page =>
    {
        var font = SkiaPageLayout.Sans(PaperTypeScale.Label);
        var width = PageFurniture.ContentWidth(page);
        var height = SkiaPageLayout.LineHeight(font);

        page.DrawLine(SkiaPageLayout.Margin, page.BottomY - height - 6,
            SkiaPageLayout.Margin + width, page.BottomY - height - 6, SkiaPageLayout.Hairline, 0.5f);

        page.DrawText(name, font, SkiaPageLayout.Margin, page.BottomY - height, width, height,
            TextAlign.TopLeft, SkiaPageLayout.InkMuted);
        page.DrawText(number.ToString(System.Globalization.CultureInfo.InvariantCulture), font,
            SkiaPageLayout.Margin, page.BottomY - height, width, height,
            TextAlign.TopRight, SkiaPageLayout.InkMuted);
    };
}
