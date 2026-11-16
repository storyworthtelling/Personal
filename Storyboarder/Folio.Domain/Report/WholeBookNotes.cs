using System.Text.RegularExpressions;
using Folio.Domain.Text;

namespace Folio.Domain.Report;

/// <summary>
/// How long a section's paragraph in the whole-book notes may run (R16): four sentences for a novel or a
/// novella, two for a short story, whose notes fit on one page. The prompt asks for that length; this is
/// what holds it when a reply runs over, cutting at a sentence's end rather than mid-thought.
/// </summary>
public static partial class WholeBookNotes
{
    public static int Sentences(StoryLength length) => length == StoryLength.ShortStory ? 2 : 4;

    public static string Summary(string? text, StoryLength length)
    {
        var trimmed = (text ?? string.Empty).Trim();
        var ends = SentenceEnd().Matches(trimmed);
        var keep = Sentences(length);
        return ends.Count <= keep ? trimmed : trimmed[..(ends[keep - 1].Index + ends[keep - 1].Length)].TrimEnd();
    }

    // A full stop, question or exclamation mark (and any closing quote) followed by a space or the end.
    [GeneratedRegex(@"[.?!][""”’)]*(?=\s|$)")]
    private static partial Regex SentenceEnd();
}
