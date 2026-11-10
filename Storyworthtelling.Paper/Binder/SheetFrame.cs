using SkiaSharp;

namespace Storyworthtelling.Paper.Binder;

/// <summary>Whose document a sheet is: the name it is signed with in the owner's hand, the identity mark the name and the
/// rule are drawn in, and the cloth and deep ink of the document's own tab.</summary>
public readonly record struct SheetSignature(string Name, SKColor Mark, SKColor Cloth, SKColor Deep);

/// <summary>
/// The frame every document sent to the tablet wears — Foreword's, by the owner's decision: the device's warm ground,
/// a Serif title with the app's name in the owner's hand at the right, the identity rule under them, the binder strip
/// down the right edge as real links, and a folio at the foot. Drawn on a <see cref="TabletSheet"/>'s board in points;
/// Foreword, Index, Constitution and Folio each say only what their sections are called and whose they are.
/// </summary>
public static class SheetFrame
{
    /// <summary>The left, top and bottom margin: three quarters of an inch.</summary>
    public const float Margin = 54f;

    public const float HeaderHeight = 22f;
    public const float RuleHeight = 2f;
    public const float RuleGap = 5f;

    /// <summary>Where a document's strip starts unless its header sits lower.</summary>
    public const float FirstTabTop = 96f;

    /// <summary>The gap between the folio's hairline and the line of words under it.</summary>
    private const float FolioRuleGap = 6f;

    /// <summary>The device's own paper, ink and hairline, at the Paper Pro profile (the bible's warm near-black and cream).</summary>
    public static readonly SKColor Ground = PaperPalette.Resolve(PaperRole.Ground, DeviceColorProfile.PaperPro);
    public static readonly SKColor Ink = PaperPalette.Resolve(PaperRole.Ink, DeviceColorProfile.PaperPro);
    public static readonly SKColor InkMuted = PaperPalette.Resolve(PaperRole.InkMuted, DeviceColorProfile.PaperPro);
    public static readonly SKColor InkSoft = PaperPalette.Resolve(PaperRole.InkSoft, DeviceColorProfile.PaperPro);
    public static readonly SKColor Hairline = PaperPalette.Resolve(PaperRole.Hairline, DeviceColorProfile.PaperPro);

    public static SKFont Serif(float size, bool black = false) =>
        Font(black ? EmbeddedTypefaces.SerifBlack : EmbeddedTypefaces.SerifSemibold, size);

    public static SKFont Sans(float size, bool bold = false) =>
        Font(bold ? EmbeddedTypefaces.SansSemibold : EmbeddedTypefaces.SansRegular, size);

    /// <summary>Pinyon Script — the owner's hand, and nothing else (DESIGN.md §4).</summary>
    public static SKFont Script(float size) => Font(EmbeddedTypefaces.Script, size);

    public static float LineHeight(SKFont font)
    {
        font.GetFontMetrics(out var m);
        return -m.Ascent + m.Descent + m.Leading;
    }

    /// <summary>The sheet's ground, edge to edge of the page.</summary>
    public static void Clear(SKCanvas canvas) => canvas.Clear(Ground);

    /// <summary>
    /// The maker's mark at a size (DESIGN.md §13): the owner's drawn oval pierced by an M, the mark Folio's covers carry,
    /// in the page's own ink on its own paper — Folio's case for a cover with no jacket. Every tablet document wears it.
    /// </summary>
    public static void Mark(SKCanvas canvas, float left, float top, float size) =>
        OwnersMark.Draw(canvas, new SKRect(left, top, left + size, top + size), Ground, Ink, Ink);

    /// <summary>The size of the mark at the head of every folio line.</summary>
    public const float FolioMarkSize = 14f;

    /// <summary>The size of the mark on a document's cover.</summary>
    public const float ColophonMarkSize = 96f;

    /// <summary>
    /// A cover's foot: the maker's mark, centred, and under it the line that says who set the paper
    /// ("SET AND DELIVERED BY FOREWORD"). <paramref name="bottom"/> is the y the line ends at; returns the mark's top.
    /// </summary>
    public static float Colophon(SKCanvas canvas, TabletSheet sheet, SheetSignature signature, float bottom, float contentWidth)
    {
        var top = bottom - ColophonMarkSize - 14f;
        Mark(canvas, Margin + (contentWidth - ColophonMarkSize) / 2f, top, ColophonMarkSize);

        var font = Sans(PaperTypeScale.Label, bold: true);
        var line = $"SET AND DELIVERED BY {signature.Name.ToUpperInvariant()}";
        using var paint = new SKPaint { Color = InkMuted, IsAntialias = true };
        font.GetFontMetrics(out var m);
        canvas.DrawText(line, Margin + (contentWidth - font.MeasureText(line)) / 2f, bottom - 12f - m.Ascent, font, paint);
        return top;
    }

    /// <summary>The title, the app's name in the owner's hand, and the identity rule. Returns the y under the rule.</summary>
    public static float Header(SKCanvas canvas, TabletSheet sheet, string title, SheetSignature signature, float top = Margin)
    {
        var width = BinderStrip.ContentRight(sheet.DesignWidth) - Margin;
        var box = new SKRect(Margin, top, Margin + width, top + HeaderHeight);

        Text(canvas, title, Serif(PaperTypeScale.Title), box, alignRight: false, Ink);
        // The app's name in the owner's hand — the one place on a document where the app signs it.
        Text(canvas, signature.Name, Script(PaperTypeScale.Title), box, alignRight: true, signature.Mark);

        var ruleTop = top + HeaderHeight + RuleGap;
        using var fill = new SKPaint { Color = signature.Mark, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(Margin, ruleTop, Margin + width, ruleTop + RuleHeight), fill);
        return ruleTop + RuleHeight + RuleGap;
    }

    /// <summary>
    /// The binder edge and every tab, each a link to its section, the active one page-coloured with its label in its own
    /// ink; the page is anchored under the active tab's name so the others' links land here. The whole tab is the
    /// target, including the one it is already on: leaving it out makes one tab inert for no reason a finger could see.
    /// </summary>
    public static void Binder(SKCanvas canvas, TabletSheet sheet, IReadOnlyList<BinderTab> tabs, int active, float firstTabTop = FirstTabTop)
    {
        canvas.DrawNamedDestinationAnnotation(new SKPoint(0, 0), tabs[active].Destination);

        var edgeX = sheet.DesignWidth - BinderStrip.BinderEdgeInset;
        using (var edge = Stroke(Hairline, BinderStrip.Hairline))
        {
            canvas.DrawLine(edgeX, 0, edgeX, sheet.DesignHeight, edge);
        }

        foreach (var box in BinderStrip.Layout(sheet.DesignWidth, tabs.Count, active, firstTabTop))
        {
            var tab = tabs[box.Index];
            var rect = new SKRect(box.Left, box.Top, box.Left + box.Width, box.Top + box.Height);
            TabShape(canvas, rect, box.Active ? Ground : tab.Cloth);
            VerticalLabel(canvas, rect, tab.Label, box.Active ? tab.ActiveInk : Ink);
            canvas.DrawLinkDestinationAnnotation(rect, tab.Destination);
        }
    }

    /// <summary>
    /// The foot of the page, in the bottom margin under the content: a hairline, the maker's mark and the page's name on
    /// the left, its number on the right.
    /// </summary>
    public static void Folio(SKCanvas canvas, TabletSheet sheet, string name, int number) =>
        Folio(canvas, sheet, name, number.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The foot of the page, with words of the caller's own (or none) on the right instead of a number.</summary>
    public static void Folio(SKCanvas canvas, TabletSheet sheet, string name, string? right)
    {
        var font = Sans(PaperTypeScale.Label);
        var width = BinderStrip.ContentRight(sheet.DesignWidth) - Margin;
        var height = LineHeight(font);
        var ruleY = FolioTop(sheet);
        var textTop = ruleY + FolioRuleGap;

        using (var rule = Stroke(Hairline, 0.5f))
        {
            canvas.DrawLine(Margin, ruleY, Margin + width, ruleY, rule);
        }

        // The maker's mark leads the line, so every page of every document carries it.
        Mark(canvas, Margin, textTop + (height - FolioMarkSize) / 2f, FolioMarkSize);
        Text(canvas, name, font, new SKRect(Margin + FolioMarkSize + 6f, textTop, Margin + width, textTop + height), alignRight: false, InkMuted);
        Text(canvas, right ?? string.Empty, font,
            new SKRect(Margin, textTop, Margin + width, textTop + height), alignRight: true, InkMuted);
    }

    /// <summary>The y of the folio's hairline, a little under the bottom margin's edge: a page's content ends above it.</summary>
    public static float FolioTop(TabletSheet sheet) => ContentBottom(sheet) + 12f;

    /// <summary>The bottom of the content area: the bottom margin's edge.</summary>
    public static float ContentBottom(TabletSheet sheet) => sheet.DesignHeight - Margin;

    /// <summary>The room a page's own content has: inside the margins, clear of the tabs, above the folio.</summary>
    public static SheetRect Body(TabletSheet sheet, float top) =>
        new(Margin, top, BinderStrip.ContentRight(sheet.DesignWidth) - Margin, ContentBottom(sheet) - top);

    /// <summary>One line of text whose box's top is <paramref name="box"/>'s top, left- or right-aligned in it.</summary>
    public static void Text(SKCanvas canvas, string text, SKFont font, SKRect box, bool alignRight, SKColor colour)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        using var paint = new SKPaint { Color = colour, IsAntialias = true };
        font.GetFontMetrics(out var m);
        var x = alignRight ? box.Right - font.MeasureText(text) : box.Left;
        canvas.DrawText(text, x, box.Top - m.Ascent, font, paint);
    }

    private static SKFont Font(SKTypeface typeface, float size) =>
        new(typeface, size) { Edging = SKFontEdging.Antialias, Subpixel = true };

    private static SKPaint Stroke(SKColor colour, float width) =>
        new() { Color = colour, StrokeWidth = width, IsAntialias = true, IsStroke = true };

    /// <summary>Square on the page side, rounded on the outer side (§10).</summary>
    private static void TabShape(SKCanvas canvas, SKRect rect, SKColor fill)
    {
        var zero = new SKPoint(0, 0);
        var round = new SKPoint(BinderStrip.Radius, BinderStrip.Radius);
        using var shape = new SKRoundRect();
        shape.SetRectRadii(rect, [zero, round, round, zero]);

        using var fillPaint = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRoundRect(shape, fillPaint);
        using var stroke = new SKPaint
        {
            Color = Hairline, StrokeWidth = BinderStrip.Hairline, IsAntialias = true, Style = SKPaintStyle.Stroke,
        };
        canvas.DrawRoundRect(shape, stroke);
    }

    /// <summary>
    /// The label runs down the tab, because a tab is 24 points wide and "Checklist" is not. Rotated rather than set in a
    /// narrow column: stacked letters are not a word, they are a puzzle.
    /// </summary>
    private static void VerticalLabel(SKCanvas canvas, SKRect tab, string text, SKColor colour)
    {
        var font = Sans(PaperTypeScale.Label, bold: true);
        var textWidth = font.MeasureText(text);

        canvas.Save();
        canvas.Translate(tab.Left + tab.Width / 2, tab.Top + (tab.Height - textWidth) / 2);
        canvas.RotateDegrees(90);
        Text(canvas, text, font, new SKRect(0, 0, tab.Height, tab.Width), alignRight: false, colour);
        canvas.Restore();
    }
}
