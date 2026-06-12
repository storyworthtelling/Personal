using Android.Graphics;

namespace FocusPhone.Launcher;

/// <summary>
/// The paper-like palette: light warm background, soft ink for icons and text,
/// and a single accent used to tint a tile while its app has a notification.
/// </summary>
public static class Palette
{
    public static readonly Color Paper = Color.ParseColor("#FAF8F2");
    public static readonly Color Ink = Color.ParseColor("#3A3A33");
    public static readonly Color InkMuted = Color.ParseColor("#8A8578");
    public static readonly Color Accent = Color.ParseColor("#C2542F");
}
