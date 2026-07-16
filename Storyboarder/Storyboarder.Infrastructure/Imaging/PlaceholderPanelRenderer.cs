using SkiaSharp;
using Storyboarder.Domain.Design;

namespace Storyboarder.Infrastructure.Imaging;

// Draws the neutral placeholder art used when a panel could not be rendered even after relaxing the
// flagged safety category and restaging the prompt to imply the content. It's a quiet shaded-paper tile
// with a faint Serif-Italic line, so the book still finishes and the panel's dialogue/caption (typeset
// by the page composer over this art) still carry the beat. Uses design tokens only (DESIGN.md §16).
public sealed class PlaceholderPanelRenderer
{
    private static readonly SKColor Paper = SKColor.Parse(DesignTokens.Paper);
    private static readonly SKColor PaperShade = SKColor.Parse(DesignTokens.PaperShade);
    private static readonly SKColor Muted = SKColor.Parse(DesignTokens.InkMuted);

    // A modest square tile; the page composer scales it to whatever panel rect it lands in.
    private const int Size = 768;

    public byte[] Render()
    {
        using var surface = SKSurface.Create(new SKImageInfo(Size, Size));
        var canvas = surface.Canvas;
        canvas.Clear(PaperShade);
        PaperGrain.Draw(canvas, Size, Size);

        // A slim inset frame so the tile reads as an intentional placeholder, not a rendering error.
        using var frame = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = DesignTokens.Comic.PanelStroke, Color = Muted, IsAntialias = true
        };
        var inset = Size * 0.08f;
        canvas.DrawRect(new SKRect(inset, inset, Size - inset, Size - inset), frame);

        using var font = new SKFont(EmbeddedTypefaces.SerifItalic, DesignTokens.Comic.CaptionSize);
        using var textPaint = new SKPaint { Color = Muted, IsAntialias = true };
        const string caption = "— art withheld —";
        var width = font.MeasureText(caption);
        canvas.DrawText(caption, (Size - width) / 2f, Size / 2f, font, textPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
