namespace Folio.Domain.Text;

/// <summary>
/// How an extractor writes a scene break the source marked without words: a horizontal rule or a page
/// break (owner's ruling, R1). Written as a divider line of its own, which <see cref="SceneDetector"/>
/// reads as a break and the marked manuscript prints where the rule was.
/// </summary>
public static class SceneBreak
{
    public const string Divider = "* * *";
}
