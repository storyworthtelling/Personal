using Android.Graphics.Drawables;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Storyworthtelling.Materials.Android;
using AView = Android.Views.View;

namespace Storyworthtelling.Maui;

/// <summary>
/// The MAUI kit's components in the drawing media of DESIGN.md §2 (W8.8 PR 2b), through the same Android drawables
/// the native kit uses, so a MAUI button and a native one are one marker.
///
/// <para>MAUI draws a Button and a Border through platform handlers, so the media are applied there: a mapping
/// appended after MAUI's own, for every property that makes MAUI rebuild the background, so the texture is put back
/// rather than silently lost when a stroke or a colour changes. <see cref="Install"/> runs once, from
/// <see cref="PaperKit"/>, which every head already merges.</para>
/// </summary>
public static class PaperMaterials
{
    private static bool installed;

    // The Border properties after whose mapping MAUI may have replaced the background drawable.
    private static readonly string[] BorderKeys =
    [
        nameof(IView.Background), nameof(IBorderStroke.Stroke), nameof(IBorderStroke.StrokeThickness), nameof(IBorderStroke.Shape),
    ];

    public static void Install()
    {
        if (installed)
        {
            return;
        }
        installed = true;

        ButtonHandler.Mapper.AppendToMapping(nameof(IView.Background), (handler, button) => Dress(handler.PlatformView, button as Button));
        foreach (var key in BorderKeys)
        {
            BorderHandler.Mapper.AppendToMapping(key, (handler, border) => Dress(handler.PlatformView, border as Border));
        }
    }

    /// <summary>
    /// §2 Highlighter: the band behind a selected layout (a picked row, the segmented control's choice, a chosen
    /// option), or none. Set on every paint, both ways, so a recycled row that is not picked says so.
    /// </summary>
    public static void Highlight(VisualElement view, Color hue, bool on)
    {
        view.BackgroundColor = Colors.Transparent;
        Apply(view, platform => platform.Background = on ? new HighlighterDrawable(hue.ToPlatform(), Density(platform)) : null);
    }

    // The primary button is a marker fill of its own (deep) colour; the secondary a pencil outline. A quiet button
    // has no ground to texture, and a button a page paints its own way is left as it is.
    private static void Dress(AView platform, Button? button)
    {
        if (button is null)
        {
            return;
        }

        Drawable? ground = null;
        if (ReferenceEquals(button.Style, PaperResources.Token<Style>("SecondaryButton")))
        {
            ground = new PencilOutlineDrawable(
                PaperResources.Colour("InkMuted").ToPlatform(), null, DesignTokens.Shape.RadiusButton, PencilLineDp, Density(platform));
        }
        else if (button.Style is null && Equals(button.BackgroundColor, PaperResources.Colour("ActionDeep")))
        {
            ground = new MarkerFillDrawable(
                PaperResources.Colour("ActionDeep").ToPlatform(),
                PaperResources.Colour("Ink").ToPlatform(),
                PaperResources.Colour("Hairline").ToPlatform(),
                DesignTokens.Shape.RadiusButton,
                Density(platform));
        }

        if (ground is null)
        {
            return;
        }

        platform.Background = ground;
        if (platform is global::Google.Android.Material.Button.MaterialButton material)
        {
            material.BackgroundTintList = null;
            material.StrokeWidth = 0;
        }
    }

    // A chip (PhaseChip, ErrorChip) is a pencil outline in its stroke's colour; a card or field on paper keeps
    // MAUI's own background (its stroke and shape) with the paper grain laid over it, under its content.
    private static void Dress(AView platform, Border? border)
    {
        if (border is null)
        {
            return;
        }

        var radius = Radius(border);
        if (IsChip(border))
        {
            var stroke = (border.Stroke as SolidColorBrush)?.Color ?? PaperResources.Colour("InkMuted");
            platform.Background = new PencilOutlineDrawable(stroke.ToPlatform(), null, radius, PencilLineDp, Density(platform));
            return;
        }

        if (!IsPaper(border.BackgroundColor))
        {
            return;
        }

        var under = platform.Background is GrainedGround grained ? grained.GetDrawable(0) : platform.Background;
        if (under is null)
        {
            return;
        }

        var grain = new PaperCardDrawable(global::Android.Graphics.Color.Transparent, true, null, radius, 0, 0, Density(platform));
        platform.Background = new GrainedGround(under, grain);
    }

    private static bool IsChip(Border border) =>
        ReferenceEquals(border.Style, PaperResources.Token<Style>("PhaseChip"))
        || ReferenceEquals(border.Style, PaperResources.Token<Style>("ErrorChip"));

    private static bool IsPaper(Color? color) =>
        color is not null
        && (Equals(color, PaperResources.Colour("Paper")) || Equals(color, PaperResources.Colour("PaperBright"))
            || Equals(color, PaperResources.Colour("PaperDeep")) || Equals(color, PaperResources.Colour("PaperShade")));

    private static float Radius(Border border) =>
        border.StrokeShape is RoundRectangle round ? (float)round.CornerRadius.TopLeft : 0;

    private static void Apply(VisualElement view, Action<AView> dress)
    {
        if (view.Handler?.PlatformView is AView ready)
        {
            dress(ready);
            return;
        }

        void Attached(object? sender, EventArgs e)
        {
            if (view.Handler?.PlatformView is AView platform)
            {
                view.HandlerChanged -= Attached;
                dress(platform);
            }
        }
        view.HandlerChanged += Attached;
    }

    private static float Density(AView platform) => platform.Resources!.DisplayMetrics!.Density;

    /// <summary>The width of a pencil line on a button or chip, in dp, as the native kit's <c>PaperUi.PencilLineDp</c>.</summary>
    public const float PencilLineDp = 1.5f;

    // MAUI's own background with the grain over it, marked so a second pass replaces the grain instead of stacking it.
    private sealed class GrainedGround(Drawable under, Drawable grain) : LayerDrawable([under, grain]);
}
