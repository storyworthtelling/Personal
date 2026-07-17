namespace Storyboarder.Domain.Imaging;

// Assembles the text prompt sent to the image model for a single comic panel. Kept here (rather than
// inline in the Gemini client) so the diagnostics capture can reproduce the exact prompt a panel was
// rendered with — the actionable artifact when a render is content-blocked and needs reworking.
public static class PanelImagePrompt
{
    public static string Build(string artStyle, string visualPrompt, bool hasReferenceImages) =>
        $"Comic panel in this style: {artStyle}. {visualPrompt}. " +
        (hasReferenceImages
            ? "Keep the characters and settings visually consistent with the supplied reference images. "
            : string.Empty) +
        "Do not render any text, speech bubbles, or lettering in the image.";
}
