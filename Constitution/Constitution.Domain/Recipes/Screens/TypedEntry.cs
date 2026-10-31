using System.Text.Json;
using Constitution.Domain.Notices;

namespace Constitution.Domain.Recipes.Screens;

/// <summary>What reading the three typed fields came to: the words, or the sentence for why there are none.</summary>
public sealed record TypedEntryRead(RecipeReadPayload? Payload, string? Sentence);

/// <summary>
/// A card typed in by hand (C-6.5.4): a title, ingredients one to a line and steps one to a line. It is the third way into the same
/// check-and-keep step a scan uses, so its words are shaped into a <see cref="RecipeReadPayload"/> by the scan contract's own
/// parse (the same bounds, the same refusals) rather than by a second set of rules. The view writes the three texts; this keeps them.
/// </summary>
public sealed class TypedEntry
{
    public const string NothingTypedSentence = "Add at least one ingredient or one step to make a card.";

    public const string Heading = "Type the card in.";

    public const string TitleLabel = "Title";

    public const string IngredientsLabel = "Ingredients, one to a line";

    public const string StepsLabel = "Steps, one to a line";

    public const string CheckAction = "Check the card";

    public const string LetGoAction = "Let go";

    public string Title { get; set; } = string.Empty;

    public string IngredientsText { get; set; } = string.Empty;

    public string StepsText { get; set; } = string.Empty;

    /// <summary>Whether nothing at all has been typed.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(IngredientsText) && string.IsNullOrWhiteSpace(StepsText);

    /// <summary>Shapes the fields into words. A refusal names the list that was refused and never quotes a word.</summary>
    public TypedEntryRead Read()
    {
        var parsed = RecipeReadContract.Parse(Title, Json(IngredientsText), Json(StepsText));
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

    /// <summary>Empties the fields. Used only by the undo and by a card that has gone on to be checked.</summary>
    public void Clear()
    {
        Title = string.Empty;
        IngredientsText = string.Empty;
        StepsText = string.Empty;
    }

    internal TypedEntry Copy() => new() { Title = Title, IngredientsText = IngredientsText, StepsText = StepsText };

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
