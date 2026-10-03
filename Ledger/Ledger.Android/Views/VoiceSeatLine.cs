using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// The line a voice seat is shown in (remediation 3.7, mockups 2, 6, 9, 11): the kit's <c>VoiceLabel</c>
/// — Pinyon Script, ink-blue, the owner's voice and nothing else's style — created hidden and revealed
/// only with a sentence in it. "Hidden" means hidden: no blank gap is left where a line would have been.
///
/// <para>The style is applied when a sentence arrives rather than at construction, because the page builds
/// its labels before it is shown and the kit's dictionary is read live (<see cref="PaperResources"/>).</para>
/// </summary>
internal static class VoiceSeatLine
{
    public static Label Create() => new() { IsVisible = false };

    /// <summary>Shows <paramref name="line"/> in the label, or hides the label when there is none or the page
    /// no longer wants one (the compose awaited Drive and the model; first run, an empty page or another
    /// side may have taken over in the meantime).</summary>
    public static void Apply(Label label, string? line, bool pageStillWantsIt)
    {
        if (string.IsNullOrWhiteSpace(line) || !pageStillWantsIt)
        {
            label.IsVisible = false;
            return;
        }

        label.Text = line;
        label.Style = PaperResources.Token<Style>("VoiceLabel");
        label.IsVisible = true;
    }
}
