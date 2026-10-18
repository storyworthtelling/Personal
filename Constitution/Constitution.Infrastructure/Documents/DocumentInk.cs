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

    public SKPaint Ink { get; } = Fill(PaperColorTokens.Ink);
    public SKPaint Shade { get; } = Fill(PaperColorTokens.PaperShade);
    public SKPaint TargetStroke { get; } = Stroke(PaperColorTokens.InkMuted, InkTargets.BoxWeight);
    public SKPaint DividerStroke { get; } = Stroke(PaperColorTokens.InkMuted, InkTargets.DividerWeight);

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
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Only text roles have a font."),
    };

    public void Dispose()
    {
        foreach (var font in new[] { Title, Dose, Body, Label })
        {
            font.Dispose();
        }

        foreach (var paint in new[] { Ink, Shade, TargetStroke, DividerStroke, HeaderRule })
        {
            paint.Dispose();
        }
    }

    private static SKPaint Fill(string hex) => new() { Color = SKColor.Parse(hex), Style = SKPaintStyle.Fill, IsAntialias = true };

    private static SKPaint Stroke(string hex, float width) =>
        new() { Color = SKColor.Parse(hex), Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };
}
