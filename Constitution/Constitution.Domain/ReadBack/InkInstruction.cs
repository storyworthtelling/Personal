using System.Globalization;
using System.Text;
using Constitution.Domain.Documents;

namespace Constitution.Domain.ReadBack;

/// <summary>
/// What the vision read is told. <see cref="System"/> is the same for every document, so a model call can
/// cache it; <see cref="Request"/> lists the boxes of one document, each described by its own words and its
/// place on the page, and nothing else about the document or the person it belongs to.
/// </summary>
public static class InkInstruction
{
    /// <summary>The standing instruction: what counts as a mark, what to say about a box, and the shape of the reply.</summary>
    public static string System { get; } =
        "You are reading pen marks on printed pages that were filled in with a stylus on an e-ink tablet. "
        + "You are given the pages as a PDF and a list of boxes to look at, each with its page number, its id, what a mark there means "
        + "and where it is on the page (in points from the top left corner of the page). "
        + "Each id is qualified by its page, like \"p2:done\" for the box \"done\" on page 2, and you answer under that whole qualified id, "
        + "with the same page number beside it. Pages are counted from 1 at the first page of the PDF.\n\n"
        + "Only pen ink drawn over the printed page is a mark. The printed words, ruled lines, outlines and shaded fields of the page are not marks.\n"
        + "Any ink in a box is a mark, however faint, small, crossed out or scribbled over. Do not judge what a mark means.\n"
        + "A circle drawn round a printed word marks the box of that word. A circle that crosses into a neighbouring box marks that box as well; report both.\n"
        + "Where something is written in a box, give it as plain text in \"text\", and give the next most likely reading, if there is one, in \"alternative\". "
        + "Write only what is on the page; do not correct it or complete it. Use null when nothing legible is written.\n"
        + "\"confidence\" is a number from 0 to 1 for how sure you are of your whole answer for that box, an empty answer included. "
        + "Give a low number whenever the ink is hard to tell from a smudge, a stray line or the printed page.\n"
        + "Answer for every box listed, once each, and for no box that is not listed.\n\n"
        + "Reply with one JSON object and nothing else:\n"
        + "{\"penStrokes\":\"seen\",\"targets\":[{\"page\":2,\"id\":\"p2:done\",\"marked\":true,\"text\":null,\"alternative\":null,\"confidence\":0.93}]}\n"
        + "\"penStrokes\" says whether you can see any handwriting or pen ink anywhere on any page, in a listed box or out of it: "
        + "\"seen\", or \"none\" when the pages look entirely unmarked, or \"unsure\".";

    /// <summary>
    /// The id a box is asked about, and answered under, in the request: its page and then its own id ("p2:done"). Every exercise
    /// page prints the same two ids, so the id alone cannot say which page a mark is on, and a model that numbers its pages
    /// from the cover instead of the first page would otherwise be believed.
    /// </summary>
    public static string QualifiedId(int page, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return "p" + page.ToString(CultureInfo.InvariantCulture) + ":" + id;
    }

    /// <summary>The boxes of this document, page by page. Empty for a document with nothing to read.</summary>
    public static string Request(ExpectedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.ReadsInk)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        text.Append("Each page is ").Append(Number(ConstitutionPage.Width)).Append(" points wide and ")
            .Append(Number(ConstitutionPage.Height)).Append(" points tall.\n");
        foreach (var page in document.Pages.OrderBy(page => page.Number))
        {
            text.Append("\nPage ").Append(page.Number.ToString(CultureInfo.InvariantCulture)).Append(":\n");
            foreach (var target in page.AskedTargets)
            {
                var box = target.Bounds;
                text.Append("- ").Append(Described(page.Number, target)).Append("; box from ")
                    .Append(Number(box.Left)).Append(',').Append(Number(box.Top)).Append(" to ")
                    .Append(Number(box.Right)).Append(',').Append(Number(box.Bottom)).Append('\n');
            }
        }

        return text.ToString();
    }

    // The target's own words with its id replaced by the qualified one: "done is this page is finished" becomes "p2:done is ...".
    private static string Described(int page, InkTarget target)
    {
        var plain = target.Describe();
        if (!plain.StartsWith(target.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"A box describes itself from its id, and '{target.Id}' is not where its description starts.");
        }

        return QualifiedId(page, target.Id) + plain[target.Id.Length..];
    }

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
