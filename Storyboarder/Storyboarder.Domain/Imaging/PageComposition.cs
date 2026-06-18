namespace Storyboarder.Domain.Imaging;

// One rendered panel plus the dialogue/caption to typeset over it. Text is drawn deterministically
// by the composer rather than asked of the image model, which renders lettering unreliably.
public sealed record PanelArt(byte[] Image, string? Dialogue, string? Caption);
