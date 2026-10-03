using SkiaSharp;
using Storyworthtelling.Paper;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// The fonts and paints both printed pages (the worksheet and the follow-up) draw with, in the Document
/// colour profile and on <see cref="PaperTypeScale"/> rungs — one place, so the two sibling painters
/// cannot drift apart on a rule weight or a face. Weights follow mockup 13's CSS: the header rule under
/// the title is 1.5pt in the identity colour, a header or section rule is 1.5pt ink, a row's rule is a
/// 1pt hairline, and the total's rule is 1pt ink.
/// </summary>
internal sealed class PageInk : IDisposable
{
    public const float HeavyRule = 1.5f;
    public const float LightRule = 1f;

    public SKFont Label { get; } = new(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Label);
    public SKFont Title { get; } = new(EmbeddedTypefaces.SerifBlack, PaperTypeScale.Display);
    public SKFont Body { get; } = new(EmbeddedTypefaces.SansRegular, PaperTypeScale.Body);
    public SKFont BodyBold { get; } = new(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Body);
    public SKFont Secondary { get; } = new(EmbeddedTypefaces.SansRegular, PaperTypeScale.Secondary);
    public SKFont SecondaryBold { get; } = new(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Secondary);
    public SKFont Mono { get; } = new(EmbeddedTypefaces.Mono, PaperTypeScale.Mono);
    public SKFont MonoBold { get; } = new(EmbeddedTypefaces.Mono, PaperTypeScale.Mono) { Embolden = true };

    public SKPaint Ink { get; } = Fill(PaperColorTokens.Ink);
    public SKPaint Soft { get; } = Fill(PaperColorTokens.InkSoft);
    public SKPaint Muted { get; } = Fill(PaperColorTokens.InkMuted);
    public SKPaint IdentityRule { get; } = Stroke(PaperColorTokens.PositiveDoc, HeavyRule);
    public SKPaint InkRule { get; } = Stroke(PaperColorTokens.Ink, HeavyRule);
    public SKPaint InkHairRule { get; } = Stroke(PaperColorTokens.Ink, LightRule);
    public SKPaint RowRule { get; } = Stroke(PaperColorTokens.Hairline, LightRule);
    public SKPaint Box { get; } = Stroke(PaperColorTokens.InkMuted, LightRule);

    public void Dispose()
    {
        foreach (var font in new[] { Label, Title, Body, BodyBold, Secondary, SecondaryBold, Mono, MonoBold })
        {
            font.Dispose();
        }
        foreach (var paint in new[] { Ink, Soft, Muted, IdentityRule, InkRule, InkHairRule, RowRule, Box })
        {
            paint.Dispose();
        }
    }

    /// <summary>A horizontal rule across <paramref name="left"/>..<paramref name="right"/> at <paramref name="y"/>.</summary>
    public static void Rule(SKCanvas canvas, SKPaint paint, float left, float right, float y) =>
        canvas.DrawLine(left, y, right, y, paint);

    /// <summary>
    /// Flows <paramref name="runs"/> as one paragraph, wrapping at <paramref name="maxWidth"/>; a run marked
    /// bold is drawn in the semibold face (mockup 13's <c>&lt;b&gt;$3,100&lt;/b&gt;</c>). Punctuation that
    /// touches a bold run stays attached to it — the break falls only at a space. Returns the baseline of
    /// the last line drawn.
    /// </summary>
    public float Paragraph(
        SKCanvas canvas, IReadOnlyList<(string Text, bool Bold)> runs, float left, float firstBaseline,
        float maxWidth, float lineHeight)
    {
        var words = new List<List<(string Text, SKFont Font)>>();
        var current = new List<(string, SKFont)>();
        foreach (var (text, bold) in runs)
        {
            var font = bold ? BodyBold : Body;
            var fragment = new System.Text.StringBuilder();
            foreach (var character in text)
            {
                if (character == ' ')
                {
                    if (fragment.Length > 0)
                    {
                        current.Add((fragment.ToString(), font));
                        fragment.Clear();
                    }
                    if (current.Count > 0)
                    {
                        words.Add(current);
                        current = [];
                    }
                }
                else
                {
                    fragment.Append(character);
                }
            }
            if (fragment.Length > 0)
            {
                current.Add((fragment.ToString(), font));
            }
        }
        if (current.Count > 0)
        {
            words.Add(current);
        }

        // Lay the words out line by line, then draw each line as whole runs (one per face) rather than a
        // word at a time: the page's text layer then reads as sentences, which is what a reader — or
        // anything extracting the text — finds.
        var spaceWidth = Body.MeasureText(" ");
        var lines = new List<List<(string Text, SKFont Font)>> { new() };
        var x = 0f;
        foreach (var word in words)
        {
            var width = word.Sum(f => f.Font.MeasureText(f.Text));
            var line = lines[^1];
            if (line.Count > 0 && x + width > maxWidth)
            {
                line = [];
                lines.Add(line);
                x = 0f;
            }
            if (line.Count > 0)
            {
                line.Add((" ", Body));
            }
            line.AddRange(word);
            x += width + spaceWidth;
        }

        var baseline = firstBaseline;
        for (var i = 0; i < lines.Count; i++)
        {
            var drawAt = left;
            var merged = new List<(string Text, SKFont Font)>();
            foreach (var (text, font) in lines[i])
            {
                if (merged.Count > 0 && ReferenceEquals(merged[^1].Font, font))
                {
                    merged[^1] = (merged[^1].Text + text, font);
                }
                else
                {
                    merged.Add((text, font));
                }
            }
            foreach (var (text, font) in merged)
            {
                canvas.DrawText(text, drawAt, baseline, font, ReferenceEquals(font, BodyBold) ? Ink : Soft);
                drawAt += font.MeasureText(text);
            }
            if (i < lines.Count - 1)
            {
                baseline += lineHeight;
            }
        }
        return baseline;
    }

    private static SKPaint Fill(string hex) => new() { Color = SKColor.Parse(hex), IsAntialias = true };

    private static SKPaint Stroke(string hex, float width) =>
        new() { Color = SKColor.Parse(hex), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };
}
