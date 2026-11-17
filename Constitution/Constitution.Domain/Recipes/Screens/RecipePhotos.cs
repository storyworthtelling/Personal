using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.Domain.Policy;

namespace Constitution.Domain.Recipes.Screens;

/// <summary>The camera a recipe page or a meal is photographed with on the owner's phone: Plate's, aimed at by package (D86, D176).</summary>
public static class PlateCamera
{
    /// <summary>Plate's frozen package. The capture intent names it on the owner's phone; the partner's uses whatever camera it has.</summary>
    public const string Package = PolicyContext.PlatePackage;
}

/// <summary>
/// The pages of one recipe, photographed one at a time (D176): Add a page, then Read the recipe. The photographs are held here as
/// re-encoded JPEG bytes, in memory only, until <see cref="Take"/> hands them to the reader; nothing is written to disk by this loop
/// and nothing is kept once they have been read.
/// </summary>
public sealed class RecipePhotoLoop
{
    /// <summary>The most pages one recipe may take. Past it the loop stops offering Add a page.</summary>
    public const int MaxPages = 8;

    public const string EmptyPhotoSentence = "That photo couldn’t be used, so the page wasn’t added.";

    private readonly List<byte[]> pages = [];
    private bool lastEmpty;

    public string AddPageAction => "Add a page";

    public string DoneAction => "Read the recipe";

    public int Pages => pages.Count;

    public bool CanFinish => pages.Count > 0;

    public bool CanAddPage => pages.Count < MaxPages;

    public string Status => lastEmpty
        ? EmptyPhotoSentence
        : pages.Count == 0 ? "No pages yet." : pages.Count + (pages.Count == 1 ? " page taken." : " pages taken.");

    /// <summary>Takes one page's photograph. An empty one is not a page, and the loop says so; past the limit nothing is added.</summary>
    public bool AddPage(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (!CanAddPage)
        {
            return false;
        }

        lastEmpty = jpeg.Length == 0;
        if (lastEmpty)
        {
            return false;
        }

        pages.Add(jpeg);
        return true;
    }

    /// <summary>Hands the pages over in the order they were taken and forgets them.</summary>
    public IReadOnlyList<byte[]> Take()
    {
        var taken = pages.ToList();
        pages.Clear();
        lastEmpty = false;
        return taken;
    }
}

/// <summary>The words of the photo route, written once. None of them quotes the card.</summary>
public static class RecipePhotoText
{
    public const string NoKeySentence = "Recipes are read with Claude, and there is no Claude key on this phone yet, so the photos weren’t read.";

    public const string NoRecipeSentence = "No recipe could be found in those photos, so nothing was filled in.";

    public const string UnreadableSentence = "The recipe in those photos couldn’t be read, so nothing was filled in.";

    public const string PageUnreadableSentence = "One of the photos couldn’t be used, so nothing was read. Take the pages again.";

    public const string TooLongSentence = "That recipe was too long to read in one go, so nothing was filled in.";

    /// <summary>The status line while the pages are read: a sentence, not a spinner.</summary>
    public static string ReadingSentence(int pages) =>
        "Reading the recipe from " + pages + (pages == 1 ? " page." : " pages.");
}

/// <summary>The scan's own sentences around the camera, written once.</summary>
public static class RecipeScanText
{
    public const string CancelledSentence = "The camera closed before a page was taken, so nothing was read.";

    /// <summary>Leads the plain reason a read failed (no network, a refused key), which follows it.</summary>
    public const string NotReadPrefix = "The recipe wasn’t read: ";
}

/// <summary>What reading the photographs came to.</summary>
public abstract record RecipePhotoRead
{
    /// <summary>The title, ingredient lines and step lines, for the three boxes.</summary>
    public sealed record Read(RecipeReadPayload Payload) : RecipePhotoRead;

    /// <summary>The read could not be asked for at all (no key); nothing was sent.</summary>
    public sealed record Unavailable(string Sentence) : RecipePhotoRead;

    /// <summary>The read was asked for, or the pages were looked at, and came to nothing usable.</summary>
    public sealed record Unreadable(string Sentence) : RecipePhotoRead;
}

/// <summary>
/// Reads a recipe off photographs of its pages (D176). The pages arrive in the order they were taken. No key is
/// <see cref="RecipePhotoRead.Unavailable"/> before anything is sent; transport and service failures are thrown with their status.
/// </summary>
public interface IRecipePhotoReader
{
    Task<RecipePhotoRead> ReadAsync(IReadOnlyList<byte[]> pages, CancellationToken cancellation);
}

/// <summary>The reader on a phone with nothing to read with: it says so and sends nothing.</summary>
public sealed class NoRecipePhotoReader : IRecipePhotoReader
{
    public Task<RecipePhotoRead> ReadAsync(IReadOnlyList<byte[]> pages, CancellationToken cancellation) =>
        Task.FromResult<RecipePhotoRead>(new RecipePhotoRead.Unavailable(RecipePhotoText.NoKeySentence));
}

/// <summary>
/// The fixed words the recipe read sends with the pages. The instruction is the same on every request (so it can be cached) and
/// names what to keep (the title, the ingredients, the steps) and what to let go (everything else on the page).
/// </summary>
public static class RecipePhotoPrompt
{
    public const string System =
        "You read a recipe from photographs of a cookbook page, a recipe card or a printout. There is one photograph per page, "
        + "in order, and the recipe may run on from one page to the next. "
        + "Keep exactly three things: the recipe’s title, its ingredients and its steps. "
        + "Copy their words as they are printed, joining any line or word the page breaks, and correcting only plain misreadings. "
        + "Leave out everything else: the story or introduction before the recipe, headnotes, tips, notes, variations, "
        + "substitutions, make-ahead and storage advice, nutrition, servings and times, serving suggestions, page numbers, "
        + "running headers and footers, captions, and any part of a different recipe that shares the page. "
        + "Ingredients: one entry per ingredient, with its quantity and unit as printed; leave out sub-headings such as \"For the sauce\". "
        + "Steps: one entry per step, in order, without its number; a step that crosses a page is one entry. "
        + "If a page shows more than one recipe, read the one that runs across the pages, else the most prominent. "
        + "Reply with one JSON object and nothing else. Its fields are: "
        + "is_recipe (true or false; false when no recipe can be read); "
        + "title (a string; empty when none is printed); "
        + "ingredients (an array of strings); "
        + "steps (an array of strings). "
        + "When is_recipe is false give only is_recipe.";

    /// <summary>The question after the pages, naming how many there are.</summary>
    public static string Question(int pages) => pages == 1
        ? "Read the recipe on this page."
        : "Read the recipe across these " + pages + " pages, in order.";
}

/// <summary>
/// Turns the model's reply into the three parts. Tolerant of a code fence or words around the object; strict about its shape
/// (lists of strings, the words' own bounds through <see cref="RecipeWords.Parse"/>). A step number the model left on is taken off.
/// A reply that makes no sense is <see cref="RecipePhotoRead.Unreadable"/>, and its text is never quoted.
/// </summary>
public static partial class RecipePhotoReply
{
    // "1. " "2) " "Step 3: " — a number then punctuation then a space, so "2 cups of water" is left alone.
    [GeneratedRegex(@"^(?:step\s*)?\d{1,2}\s*[.):\-]\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex StepNumber();

    public static RecipePhotoRead Parse(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return Unreadable();
        }

        var start = reply.IndexOf('{', StringComparison.Ordinal);
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return Unreadable();
        }

        try
        {
            using var document = JsonDocument.Parse(reply[start..(end + 1)]);
            var root = document.RootElement;
            if (!root.TryGetProperty("is_recipe", out var isRecipe) || isRecipe.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Unreadable();
            }

            if (isRecipe.ValueKind == JsonValueKind.False)
            {
                return new RecipePhotoRead.Unreadable(RecipePhotoText.NoRecipeSentence);
            }

            var title = root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString()
                : null;
            if (!TryList(root, "ingredients", out var ingredients) || !TryList(root, "steps", out var steps))
            {
                return Unreadable();
            }

            var parsed = RecipeWords.Parse(
                title,
                JsonSerializer.Serialize(ingredients),
                JsonSerializer.Serialize(steps.Select(step => StepNumber().Replace(step.Trim(), string.Empty, 1)).ToList()));
            if (parsed.Payload is { } payload)
            {
                return new RecipePhotoRead.Read(payload);
            }

            return parsed.RefusedField == "recipe"
                ? new RecipePhotoRead.Unreadable(RecipePhotoText.NoRecipeSentence)
                : Unreadable();
        }
        catch (JsonException)
        {
            // Not JSON after all; the reply's words are never echoed.
            return Unreadable();
        }
    }

    private static RecipePhotoRead Unreadable() => new RecipePhotoRead.Unreadable(RecipePhotoText.UnreadableSentence);

    // A missing list is an empty one; a list that is anything but strings is a reply that makes no sense.
    private static bool TryList(JsonElement root, string name, out List<string> lines)
    {
        lines = [];
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            lines.Add(item.GetString()!);
        }

        return true;
    }
}
