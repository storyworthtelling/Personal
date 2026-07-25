using Storyboarder.Domain.Model;

namespace Storyboarder.Domain.Imaging;

// One rendered panel plus the dialogue/caption to typeset over it, and how it's framed (which sizes it
// on the page). Text is drawn deterministically by the composer rather than asked of the image model,
// which renders lettering unreliably. Shot defaults to Standard, so a caller that doesn't set it gets
// the uniform grid.
public sealed record PanelArt(byte[] Image, string? Dialogue, string? Caption, ShotType Shot = ShotType.Standard);
