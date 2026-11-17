using System.Text.Json;
using Constitution.Domain.Notices;

namespace Constitution.Domain.Recipes.Screens;

/// <summary>What reading the three typed fields came to: the words, or the sentence for why there are none.</summary>
public sealed record TypedEntryRead(RecipeReadPayload? Payload, string? Sentence);

/// <summary>
/// The three boxes a card is made in (C-6.5.4, D176): a title, ingredients one to a line and steps one to a line. A card typed by
/// hand starts them empty; a scanned card starts them filled with what was read off its photographs (<see cref="Fill"/>), and the
/// owner edits either before keeping it. The words are shaped into a <see cref="RecipeReadPayload"/> by <see cref="RecipeWords.Parse"/>
/// (the same bounds, the same refusals) rather than by a second set of rules. The view writes the three texts; this keeps them.
/// </summary>
public sealed class TypedEntry
{
    public const string NothingTypedSentence = "Add at least one ingredient or one step to make a card.";

    public const string Heading = "Type the card in.";

    public const string ReadHeading = "Check what was read.";

    public const string ReadNote = "Edit anything that is wrong. The photos aren’t kept, only these words.";

    public const string TitleLabel = "Title";

    public const string IngredientsLabel = "Ingredients, one to a line";

    public const string StepsLabel = "Steps, one to a line";

    public const string KeepAction = "Keep the card";

    public const string LetGoAction = "Let go";

    public string Title { get; set; } = string.Empty;

    public string IngredientsText { get; set; } = string.Empty;

    public string StepsText { get; set; } = string.Empty;

    /// <summary>Whether the boxes were filled from photographs rather than typed from nothing.</summary>
    public bool FromPhotos { get; private set; }

    /// <summary>The page's heading: checking what was read, or typing it in.</summary>
    public string PageHeading => FromPhotos ? ReadHeading : Heading;

    /// <summary>Where the card is kept from: a read of photographs is a scanned card, anything else a typed one.</summary>
    public ScanOrigin Origin => FromPhotos ? ScanOrigin.Scanned : ScanOrigin.Typed;

    /// <summary>Whether nothing at all has been typed.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(IngredientsText) && string.IsNullOrWhiteSpace(StepsText);

    /// <summary>Shapes the fields into words. A refusal names the list that was refused and never quotes a word.</summary>
    public TypedEntryRead Read()
    {
        var parsed = RecipeWords.Parse(Title, Json(IngredientsText), Json(StepsText));
        if (parsed.Payload is { } payload)
        {
            return new TypedEntryRead(payload, null);
        }

        var sentence = parsed.RefusedField switch
        {
            "recipe" => NothingTypedSentence,
            "title" => "The title is too long, so shorten it and check again.",
            _ => "The " + parsed.RefusedField + " are too long to be a card, so shorten them and check again.",
        };
        return new TypedEntryRead(null, sentence);
    }

    /// <summary>Fills the boxes with what was read off the photographs, one ingredient and one step to a line, for the owner to edit.</summary>
    public void Fill(RecipeReadPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Title = payload.Title;
        IngredientsText = string.Join("\n", payload.Ingredients);
        StepsText = string.Join("\n", payload.Steps);
        FromPhotos = true;
    }

    /// <summary>Puts back the words an undo held, and where they came from.</summary>
    public void Restore(TypedEntry words)
    {
        ArgumentNullException.ThrowIfNull(words);
        Title = words.Title;
        IngredientsText = words.IngredientsText;
        StepsText = words.StepsText;
        FromPhotos = words.FromPhotos;
    }

    /// <summary>Empties the fields. Used only by the undo and by a card that has gone on to be kept.</summary>
    public void Clear()
    {
        Title = string.Empty;
        IngredientsText = string.Empty;
        StepsText = string.Empty;
        FromPhotos = false;
    }

    internal TypedEntry Copy() => new() { Title = Title, IngredientsText = IngredientsText, StepsText = StepsText, FromPhotos = FromPhotos };

    private static string Json(string text) =>
        JsonSerializer.Serialize(text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList());
}

/// <summary>
/// Letting go of typed words, undo-first (DESIGN.md section 5; the same shape as <see cref="ScanDiscardUndo"/>). The fields empty at
/// once, the shared dog-ear holds the words for its window, a turn of the fold hands them back, and when the window closes they are
/// forgotten. Nothing is asked. Time is passed in.
/// </summary>
public sealed class TypedEntryUndo
{
    public const string ActKey = "recipe-typed:discard";

    private DateTimeOffset expires;

    /// <summary>The words inside their undo window, if there are any.</summary>
    public TypedEntry? Pending { get; private set; }

    /// <summary>Empties <paramref name="entry"/> and holds its words. False, with no window, when there was nothing typed.</summary>
    public bool Begin(NoticeBoard board, TypedEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsEmpty)
        {
            return false;
        }

        Pending = entry.Copy();
        entry.Clear();
        expires = now + NoticeBoard.UndoWindow;
        board.BeginUndo(ActKey, now);
        return true;
    }

    /// <summary>The fold was turned and the board gave back this act key: the words to bring back, or null when the act was not ours.</summary>
    public TypedEntry? Reverse(string actKey)
    {
        if (Pending is not { } waiting || actKey != ActKey)
        {
            return null;
        }

        Pending = null;
        return waiting;
    }

    /// <summary>The window has closed: the words are forgotten. Returns whether there were any to let go.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Pending is null || now < expires)
        {
            return false;
        }

        Pending = null;
        return true;
    }
}
