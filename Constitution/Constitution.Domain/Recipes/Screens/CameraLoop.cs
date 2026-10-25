using System.Text.Json;
using Codex.Domain.Plate;

namespace Constitution.Domain.Recipes.Screens;

/// <summary>
/// The seam to the partner's phone reading a photographed page into words (C-6.5.2, D105). An implementation recognises the text of a
/// photograph on the phone and lets the photograph go the moment it has the lines, whatever the reading came to; what crosses this
/// seam is a path in and text out, and no picture. <see cref="NoRecipeTextReader"/> stands in on a phone that cannot read a card.
/// </summary>
public interface IRecipeTextReader
{
    /// <summary>Whether this phone can read a card page at all. The picker shows the camera row live only if it can.</summary>
    bool CanRead { get; }

    /// <summary>
    /// One page's lines in reading order. The photograph at <paramref name="photoPath"/> is deleted before this returns or throws.
    /// A failure to read is thrown with its stack, not turned into an empty page.
    /// </summary>
    Task<IReadOnlyList<string>> ReadPhotoAsync(string photoPath);
}

/// <summary>The reader used on a phone that cannot read a card: nothing is offered, and asking anyway says so.</summary>
public sealed class NoRecipeTextReader : IRecipeTextReader
{
    public bool CanRead => false;

    public Task<IReadOnlyList<string>> ReadPhotoAsync(string photoPath) =>
        throw new InvalidOperationException("This phone cannot read a recipe card, so no photograph was read.");
}

/// <summary>
/// One photo per page, then Add a page or Done (C-6.5.2, book p.14). The loop holds the lines of the pages read so far and turns
/// them, on Done, into the same answer Plate gives: the title, the ingredient lines and the step lines, found by the same
/// card-reading rules Plate uses (<see cref="RecipeRead"/>), judged by the same contract. It forgets every line when it finishes.
/// </summary>
public sealed class CameraLoop
{
    public const string EmptyPageSentence = "No words could be read on that photo, so the page was not added.";

    public const string NothingOnTheCardSentence = "No ingredients or steps could be read from those photos, so nothing was kept.";

    private readonly List<string> lines = [];
    private bool lastPageEmpty;

    public string AddPageAction => "Add a page";

    public string DoneAction => "Done";

    public int Pages { get; private set; }

    /// <summary>Done is offered once a page has been read.</summary>
    public bool CanFinish => Pages > 0;

    public string Status => lastPageEmpty
        ? EmptyPageSentence
        : Pages == 0 ? "No pages yet." : Pages + (Pages == 1 ? " page read." : " pages read.");

    /// <summary>Takes one page's lines. A page with no words in it is not a page, and the loop says so.</summary>
    public bool AddPage(IReadOnlyList<string> pageLines)
    {
        ArgumentNullException.ThrowIfNull(pageLines);
        var words = pageLines.Select(line => line?.Trim() ?? string.Empty).Where(line => line.Length > 0).ToList();
        lastPageEmpty = words.Count == 0;
        if (lastPageEmpty)
        {
            return false;
        }

        lines.AddRange(words);
        Pages++;
        return true;
    }

    /// <summary>Done. With no pages it is a back-out; otherwise the words, or the sentence for why there are none. Every line is forgotten.</summary>
    public ScanReturn Finish()
    {
        if (Pages == 0)
        {
            return new ScanReturn.Cancelled();
        }

        var reading = RecipeRead.Read(lines);
        lines.Clear();
        Pages = 0;
        lastPageEmpty = false;

        var parsed = RecipeReadContract.Parse(
            reading.Title,
            JsonSerializer.Serialize(reading.Ingredients),
            JsonSerializer.Serialize(reading.Steps));
        if (parsed.Payload is { } payload)
        {
            return new ScanReturn.Read(payload);
        }

        return new ScanReturn.Refused(parsed.RefusedField == "recipe"
            ? NothingOnTheCardSentence
            : "The words from those photos could not be used (" + parsed.RefusedField + "), so nothing was kept.");
    }
}
