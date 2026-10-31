using Constitution.Domain.Documents;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Constitution.Infrastructure.Documents;

/// <summary>
/// The fonts and paints every Constitution page draws with, in one place so two painters cannot drift
/// apart on a weight or a face. Sizes are rungs of <see cref="PaperTypeScale"/>; colours are the
/// Document-profile tokens. Targets and dividers print in the muted ink so the pen's black always
/// stands out from the print on a greyscale panel; the header rule is the only coloured stroke.
/// </summary>
internal sealed class DocumentInk : IDisposable
{
    public SKFont Title { get; } = new(EmbeddedTypefaces.SerifBlack, PaperTypeScale.Title);
    public SKFont Dose { get; } = new(EmbeddedTypefaces.SerifBlack, PaperTypeScale.Dose);
    public SKFont Body { get; } = new(EmbeddedTypefaces.SansRegular, PaperTypeScale.Body);
    public SKFont Label { get; } = new(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Label);
    public SKFont Heading { get; } = new(EmbeddedTypefaces.SerifSemibold, PaperTypeScale.Heading);
    public SKFont Choice { get; } = new(EmbeddedTypefaces.SerifRegular, PaperTypeScale.Heading);
    public SKFont Strong { get; } = new(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Body);
    public SKFont Small { get; } = new(EmbeddedTypefaces.SansRegular, PaperTypeScale.Secondary);

    /// <summary>Cascadia Code on the Mono rung: the recipe's quantity column.</summary>
    public SKFont Mono { get; } = new(EmbeddedTypefaces.Mono, PaperTypeScale.Mono);

    /// <summary>Cascadia Code on the Title rung: the recipe's step times, 21 points on the counter.</summary>
    public SKFont Time { get; } = new(EmbeddedTypefaces.Mono, PaperTypeScale.Title);

    /// <summary>The owner's hand (Pinyon Script) on the Title rung: the one place the encouragement seat prints.</summary>
    public SKFont Voice { get; } = new(EmbeddedTypefaces.Script, PaperTypeScale.Title);

    public SKPaint Ink { get; } = Fill(PaperColorTokens.Ink);
    public SKPaint Soft { get; } = Fill(PaperColorTokens.InkSoft);
    public SKPaint Shade { get; } = Fill(PaperColorTokens.PaperShade);

    /// <summary>The bright paper a tick is filled with where it sits on a shaded band, so the box stays a white target.</summary>
    public SKPaint Bright { get; } = Fill(PaperColorTokens.PaperBright);
    public SKPaint TargetStroke { get; } = Stroke(PaperColorTokens.InkMuted, InkTargets.BoxWeight);
    public SKPaint DividerStroke { get; } = Stroke(PaperColorTokens.InkMuted, InkTargets.DividerWeight);

    /// <summary>The notes column's rules: the same hairline, dotted so they read as lines to write on and not as dividers.</summary>
    public SKPaint DottedStroke { get; } = Dotted();

    /// <summary>The digest's weekly line: the ink itself at a pen's weight, neutral, never coloured.</summary>
    public SKPaint ChartStroke { get; } = Stroke(PaperColorTokens.Ink, 1.2f);

    public SKPaint HeaderRule { get; } = new()
    {
        Color = AppIdentityTokens.ConstitutionDocument,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = ConstitutionPage.HeaderRuleWeight,
        IsAntialias = true,
    };

    public SKFont FontFor(PrintedRole role) => role switch
    {
        PrintedRole.Dose => Dose,
        PrintedRole.Body => Body,
        PrintedRole.Label => Label,
        PrintedRole.Heading => Heading,
        PrintedRole.Choice => Choice,
        PrintedRole.Strong => Strong,
        PrintedRole.Small => Small,
        PrintedRole.Voice => Voice,
        PrintedRole.Mono => Mono,
        PrintedRole.Time => Time,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Only text roles have a font."),
    };

    public void Dispose()
    {
        foreach (var font in new[] { Title, Dose, Body, Label, Heading, Choice, Strong, Small, Mono, Time, Voice })
        {
            font.Dispose();
        }

        foreach (var paint in new[] { Ink, Soft, Shade, Bright, TargetStroke, DividerStroke, DottedStroke, ChartStroke, HeaderRule })
        {
            paint.Dispose();
        }
    }

    private static SKPaint Dotted()
    {
        var paint = Stroke(PaperColorTokens.InkMuted, InkTargets.DividerWeight);
        paint.PathEffect = SKPathEffect.CreateDash([InkTargets.DividerWeight * 3f, InkTargets.DividerWeight * 6f], 0f);
        return paint;
    }

    private static SKPaint Fill(string hex) => new() { Color = SKColor.Parse(hex), Style = SKPaintStyle.Fill, IsAntialias = true };

    private static SKPaint Stroke(string hex, float width) =>
        new() { Color = SKColor.Parse(hex), Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };
}
