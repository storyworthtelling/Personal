namespace Storyboarder.Domain.Imaging;

// Assembles the text prompt sent to the image model for a single comic panel. Kept here (rather than
// inline in the Gemini client) so the diagnostics capture can reproduce the exact prompt a panel was
// rendered with — the actionable artifact when a render is content-blocked and needs reworking.
public static class PanelImagePrompt
{
    public static string Build(string artStyle, string visualPrompt, bool hasReferenceImages) =>
        $"Comic panel in this style: {artStyle}. {visualPrompt}. " +
        "Show the characters actively in this moment — caught mid-action and engaged with the scene, " +
        "not posing or facing the camera. " +
        (hasReferenceImages
            ? "Match each character's and setting's appearance from the supplied reference images, but " +
              "not their pose — stage them in the action described, at a consistent relative scale. "
            : string.Empty) +
        "Do not render any text, speech bubbles, or lettering in the image.";
}
