using System.Globalization;
using System.Text.RegularExpressions;

namespace Constitution.Domain.Recipes.Screens;

/// <summary>A stretch of a scanned line, and whether it is a word the owner is being asked about (drawn under the Caution highlighter wash).</summary>
public sealed record ScanSegment(string Text, bool Marked);

/// <summary>One line of the card as it reads now, with the choices already applied.</summary>
public sealed record ScanCardLine(ScanLineKind Kind, int Index, string Text, IReadOnlyList<ScanSegment> Segments);

/// <summary>
/// One choice row (book p.14): a short label, the best reading first then up to two alternatives, and which is chosen.
/// <see cref="Selected"/> is an index into <see cref="Options"/>, or <see cref="OtherIndex"/> when the owner typed their own words.
/// The "Other…" chip is always offered after the options.
/// </summary>
public sealed record ScanQuestion(string Label, IReadOnlyList<string> Options, int Selected, string? OtherText)
{
    /// <summary>Selected when the owner typed the word instead of choosing one.</summary>
    public const int OtherIndex = -1;

    public bool IsOther => Selected == OtherIndex;
}

/// <summary>The words of the question block. Static copy.</summary>
public static class ScanQuestionText
{
    public const string OtherOption = "Other…";

    private static readonly string[] Number = ["Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten"];

    /// <summary>"Three words were hard to read": the count in words to ten, in digits after.</summary>
    public static string Heading(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var number = count < Number.Length ? Number[count] : count.ToString(CultureInfo.InvariantCulture);
        return count == 1 ? number + " word was hard to read" : number + " words were hard to read";
    }
}

/// <summary>How asking to keep the card came out.</summary>
public enum ScanKeepOutcome
{
    /// <summary>The keeper took the card.</summary>
    Kept,

    /// <summary>The card had already been kept; nothing was written twice.</summary>
    AlreadyKept,

    /// <summary>The draft is not a card yet (no title, a figure out of range); the sentence says what.</summary>
    NeedsAttention,

    /// <summary>The keeper could not write it; the draft is still here to try again.</summary>
    NotKept,

    /// <summary>The draft was discarded, so there is nothing to keep.</summary>
    NothingToKeep,
}

public sealed record ScanKeepResult(ScanKeepOutcome Outcome, Recipe? Recipe, string? Sentence);

/// <summary>An id for a scanned card: the title's lower-case words joined by dashes, made unique among the cards already in the box.</summary>
public static class ScanRecipeIds
{
    public const string Fallback = "scanned-card";

    private const int BaseLength = 56;

    private static readonly Regex NotAWord = new("[^a-z0-9]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static RecipeId For(string title, IReadOnlySet<RecipeId> existing)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(existing);
        var folded = SearchText.Fold(title).Replace("'", string.Empty, StringComparison.Ordinal).Replace("’", string.Empty, StringComparison.Ordinal);
        var stem = NotAWord.Replace(folded, "-").Trim('-');
        if (stem.Length > BaseLength)
        {
            stem = stem[..BaseLength].TrimEnd('-');
        }

        if (stem.Length == 0)
        {
            stem = Fallback;
        }

        var candidate = new RecipeId(stem);
        for (var suffix = 2; existing.Contains(candidate); suffix++)
        {
            candidate = new RecipeId(stem + "-" + suffix.ToString(CultureInfo.InvariantCulture));
        }

        return candidate;
    }
}

/// <summary>
/// The scanned card, checked (C-6.5.3, book p.14, D63, D64, D99). Plate returns words only; this turns them into an editable draft
/// that the owner checks while the card is still in hand, and <b>keeps only the words</b>: nothing here holds a picture, and
/// nothing is written anywhere until <see cref="Keep"/> (Looks right) hands a validated <see cref="Recipe"/> to the
/// <see cref="IRecipeKeeper"/>. <see cref="Discard"/> forgets every word.
///
/// <para>What the owner may change is what p.14 lets them: the title when the card gave none, the answer to each doubted word
/// (the best reading is already chosen; two alternatives; "Other…" to type), and, outside the drawn page, servings and cuisine. Nothing
/// else on the card is asked about. The cuisine starts as <see cref="Cuisine.Other"/> and is assigned silently, movable later from the
/// box (D64). With no judge (<see cref="NoRecipeWordJudge"/>) there are no doubts, and a clean scan still shows its card once with
/// a single Looks right (D63).</para>
///
/// <para>Validation is the committed recipe's: the draft is built into a <see cref="Recipe"/> and the sentence for what it refuses is
/// <see cref="Problem"/>. Not thread safe; the head uses it from one place at a time.</para>
/// </summary>
public sealed class ScanConfirmModel
{
    public const string TitleSentence = "Give the card a title.";
    public const string NotKeptSentence = "That card could not be kept, so it is still here to try again.";

    private readonly List<string> ingredientLines;
    private readonly List<string> stepLines;
    private readonly List<Doubt> doubts;
    private string title;
    private bool discarded;

    private ScanConfirmModel(RecipeReadPayload payload, List<Doubt> doubts)
    {
        title = payload.Title;
        ingredientLines = [.. payload.Ingredients];
        stepLines = [.. payload.Steps];
        this.doubts = doubts;
    }

    // ---- the page's words ----

    /// <summary>The small label over the card.</summary>
    public string Label => "Just scanned";

    /// <summary>The line under the header: what is and is not kept.</summary>
    public string StatusSentence => "The picture isn’t kept, only the words.";

    /// <summary>The page's primary action, pinned in the bar.</summary>
    public string KeepAction => "Looks right";

    /// <summary>The page's secondary action: read the card again.</summary>
    public string ScanAgainAction => "Scan again";

    // ---- the draft ----

    public string Title => title;

    public Cuisine Cuisine { get; private set; } = Cuisine.Other;

    public int? Servings { get; private set; }

    /// <summary>Whether the card gave no title, so the page asks for one in place.</summary>
    public bool NeedsTitle => !discarded && string.IsNullOrWhiteSpace(title);

    public bool IsKept { get; private set; }

    public bool IsDiscarded => discarded;

    public IReadOnlyList<ScanIngredientDraft> Ingredients =>
        ingredientLines.Select((_, index) => ScanLineParser.Ingredient(TextOf(ScanLineKind.Ingredient, index))).ToList();

    public IReadOnlyList<ScanStepDraft> Steps =>
        stepLines.Select((_, index) => ScanLineParser.Step(TextOf(ScanLineKind.Step, index))).ToList();

    /// <summary>The card as it shows: ingredients then steps, each with the doubted words marked.</summary>
    public IReadOnlyList<ScanCardLine> Lines =>
        ingredientLines.Select((_, index) => LineOf(ScanLineKind.Ingredient, index))
            .Concat(stepLines.Select((_, index) => LineOf(ScanLineKind.Step, index)))
            .ToList();

    /// <summary>One row per doubted word, in the order they sit on the card. Empty when nothing is doubted.</summary>
    public IReadOnlyList<ScanQuestion> Questions =>
        doubts.Select(doubt => new ScanQuestion(doubt.Label, doubt.Options, doubt.Selected, doubt.OtherText)).ToList();

    /// <summary>"Three words were hard to read", or null when no word is.</summary>
    public string? QuestionHeading => doubts.Count == 0 ? null : ScanQuestionText.Heading(doubts.Count);

    /// <summary>The sentence for what stops the draft being a card, or null when it can be kept.</summary>
    public string? Problem
    {
        get
        {
            if (discarded || IsKept)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return TitleSentence;
            }

            try
            {
                Build(new RecipeId("scanned-card"));
                return null;
            }
            catch (ArgumentException e)
            {
                // The committed rules name what is wrong without quoting the card; the framework's parameter suffix is dropped.
                var cut = e.Message.IndexOf(" (Parameter", StringComparison.Ordinal);
                return cut > 0 ? e.Message[..cut] : e.Message;
            }
        }
    }

    // ---- building ----

    /// <summary>
    /// The draft for a payload. The judge, when there is one, is asked once about the words alone; what it doubts is kept only if
    /// the doubted word really is on the line it names.
    /// </summary>
    public static ScanConfirmModel From(RecipeReadPayload payload, IRecipeWordJudge? judge = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var words = judge?.Judge(payload) ?? [];
        return new ScanConfirmModel(payload, Accept(payload, words));
    }

    private static List<Doubt> Accept(RecipeReadPayload payload, IReadOnlyList<UncertainWord> words)
    {
        var accepted = new List<Doubt>();
        foreach (var word in words)
        {
            var lines = word.Kind == ScanLineKind.Ingredient ? payload.Ingredients : payload.Steps;
            var reading = word.Reading?.Trim() ?? string.Empty;
            if (reading.Length == 0 || word.LineIndex < 0 || word.LineIndex >= lines.Count)
            {
                continue;
            }

            var start = lines[word.LineIndex].IndexOf(reading, StringComparison.Ordinal);
            if (start < 0 || accepted.Any(other => other.Overlaps(word.Kind, word.LineIndex, start, reading.Length)))
            {
                continue;
            }

            var alternatives = (word.Alternatives ?? [])
                .Select(alternative => alternative?.Trim() ?? string.Empty)
                .Where(alternative => alternative.Length > 0 && !alternative.Equals(reading, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2);
            var label = string.IsNullOrWhiteSpace(word.Label) ? reading : word.Label.Trim();
            accepted.Add(new Doubt(word.Kind, word.LineIndex, start, reading.Length, label, [reading, .. alternatives]));
        }

        return accepted
            .OrderBy(doubt => doubt.Kind)
            .ThenBy(doubt => doubt.LineIndex)
            .ThenBy(doubt => doubt.Start)
            .ToList();
    }

    // ---- the owner's changes ----

    public void SetTitle(string? text)
    {
        if (Editable)
        {
            title = text?.Trim() ?? string.Empty;
        }
    }

    public void ChooseCuisine(Cuisine cuisine)
    {
        if (Editable)
        {
            Cuisine = cuisine;
        }
    }

    public void SetServings(int? servings)
    {
        if (Editable)
        {
            Servings = servings;
        }
    }

    /// <summary>Chooses option <paramref name="option"/> of question <paramref name="question"/>. False when either is out of range.</summary>
    public bool Choose(int question, int option)
    {
        if (!Editable || question < 0 || question >= doubts.Count || option < 0 || option >= doubts[question].Options.Count)
        {
            return false;
        }

        doubts[question].Selected = option;
        doubts[question].OtherText = null;
        return true;
    }

    /// <summary>Takes the owner's own words for question <paramref name="question"/>. False for blank words or a question that is not there.</summary>
    public bool ChooseOther(int question, string? text)
    {
        if (!Editable || question < 0 || question >= doubts.Count || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        doubts[question].Selected = ScanQuestion.OtherIndex;
        doubts[question].OtherText = text.Trim();
        return true;
    }

    /// <summary>Forgets every word. A card already kept is not affected, and a discarded one stays discarded.</summary>
    public void Discard()
    {
        if (IsKept || discarded)
        {
            return;
        }

        discarded = true;
        title = string.Empty;
        ingredientLines.Clear();
        stepLines.Clear();
        doubts.Clear();
        Servings = null;
        Cuisine = Cuisine.Other;
    }

    /// <summary>
    /// Looks right: builds the card and hands it to <paramref name="keeper"/>, the only way a scanned card is ever kept. A card
    /// that cannot be written leaves the draft as it was, with a sentence naming the failure by type and never its message.
    /// Anything other than an I/O or access failure is a bug and is not swallowed.
    /// </summary>
    /// <param name="keeper">Where the card goes.</param>
    /// <param name="existing">The ids already in the box, so the new card's id is its own.</param>
    public ScanKeepResult Keep(IRecipeKeeper keeper, IReadOnlySet<RecipeId> existing)
    {
        ArgumentNullException.ThrowIfNull(keeper);
        ArgumentNullException.ThrowIfNull(existing);
        if (IsKept)
        {
            return new ScanKeepResult(ScanKeepOutcome.AlreadyKept, null, null);
        }

        if (discarded)
        {
            return new ScanKeepResult(ScanKeepOutcome.NothingToKeep, null, null);
        }

        if (Problem is { } problem)
        {
            return new ScanKeepResult(ScanKeepOutcome.NeedsAttention, null, problem);
        }

        var card = Build(ScanRecipeIds.For(title, existing));
        RecipeKeepResult kept;
        try
        {
            kept = keeper.Keep(card);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ScanKeepResult(ScanKeepOutcome.NotKept, null, NotKeptSentence + " (" + e.GetType().Name + ")");
        }

        if (!kept.IsKept)
        {
            return new ScanKeepResult(ScanKeepOutcome.NotKept, null, kept.Refusal);
        }

        IsKept = true;
        return new ScanKeepResult(ScanKeepOutcome.Kept, card, null);
    }

    // ---- internals ----

    private bool Editable => !discarded && !IsKept;

    private Recipe Build(RecipeId id) => new(
        id,
        title,
        Cuisine,
        Ingredients.Select(draft => new Ingredient(draft.Quantity, draft.Unit, draft.Name)),
        Steps.Select(draft => new RecipeStep(draft.Text, draft.Minutes, active: false)),
        Servings,
        RecipeSource.Scanned,
        forKids: false);

    private string TextOf(ScanLineKind kind, int index) =>
        string.Concat(SegmentsOf(kind, index).Select(segment => segment.Text));

    private ScanCardLine LineOf(ScanLineKind kind, int index)
    {
        var segments = SegmentsOf(kind, index);
        return new ScanCardLine(kind, index, string.Concat(segments.Select(segment => segment.Text)), segments);
    }

    private List<ScanSegment> SegmentsOf(ScanLineKind kind, int index)
    {
        var original = (kind == ScanLineKind.Ingredient ? ingredientLines : stepLines)[index];
        var segments = new List<ScanSegment>();
        var cursor = 0;
        foreach (var doubt in doubts.Where(doubt => doubt.Kind == kind && doubt.LineIndex == index))
        {
            if (doubt.Start > cursor)
            {
                segments.Add(new ScanSegment(original[cursor..doubt.Start], false));
            }

            segments.Add(new ScanSegment(doubt.Chosen, true));
            cursor = doubt.Start + doubt.Length;
        }

        if (cursor < original.Length || segments.Count == 0)
        {
            segments.Add(new ScanSegment(original[cursor..], false));
        }

        return segments;
    }

    private sealed class Doubt(ScanLineKind kind, int lineIndex, int start, int length, string label, IReadOnlyList<string> options)
    {
        public ScanLineKind Kind { get; } = kind;

        public int LineIndex { get; } = lineIndex;

        public int Start { get; } = start;

        public int Length { get; } = length;

        public string Label { get; } = label;

        public IReadOnlyList<string> Options { get; } = options;

        public int Selected { get; set; }

        public string? OtherText { get; set; }

        public string Chosen => Selected == ScanQuestion.OtherIndex ? OtherText! : Options[Selected];

        public bool Overlaps(ScanLineKind otherKind, int otherLine, int otherStart, int otherLength) =>
            Kind == otherKind && LineIndex == otherLine && otherStart < Start + Length && Start < otherStart + otherLength;
    }
}
