using Constitution.Domain.Documents;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Templates;

/// <summary>Which of the sheet's meals a template is for.</summary>
public enum MealTemplateKind
{
    Breakfast,
    Lunch,
    Lunchbox,
}

/// <summary>
/// The line of the sheet a template fills. Weekday and weekend lines are separate slots (D31), and the lunchbox is its five
/// parts, each rotated on its own, because <see cref="MealSheetLunchbox"/> needs a sandwich, a fruit, a snack and a separate
/// fruit-or-veg and bar rather than one boxed idea.
/// </summary>
public enum MealTemplateSlot
{
    WeekdayBreakfast,
    WeekdayLunch,
    WeekendBreakfast,
    WeekendLunch,
    BoxSandwich,
    BoxFruit,
    BoxSnack,
    SnackFruitOrVeg,
    SnackBar,
}

/// <summary>Where a template stands: proposed (not yet answered), approved (in the rotation) or rejected (never printed again).</summary>
public enum TemplateStatus
{
    Proposed,
    Approved,
    Rejected,
}

/// <summary>How an approved template was approved. A tick is a statement; silence at the deadline is only the absence of one (D103).</summary>
public enum TemplateApproval
{
    /// <summary>Not approved.</summary>
    None,

    /// <summary>An adult ticked the line and nobody denied it.</summary>
    Explicit,

    /// <summary>Nobody ticked or denied it and the deadline passed: approved, but it says nothing about taste.</summary>
    Silence,
}

/// <summary>The stored words for a template's enums. <b>Frozen once shipped</b>: literals, never enum names.</summary>
public static class MealTemplateWords
{
    public static FieldWords<MealTemplateSlot> Slots { get; } = new(new Dictionary<MealTemplateSlot, string>
    {
        [MealTemplateSlot.WeekdayBreakfast] = "weekday-breakfast",
        [MealTemplateSlot.WeekdayLunch] = "weekday-lunch",
        [MealTemplateSlot.WeekendBreakfast] = "weekend-breakfast",
        [MealTemplateSlot.WeekendLunch] = "weekend-lunch",
        [MealTemplateSlot.BoxSandwich] = "box-sandwich",
        [MealTemplateSlot.BoxFruit] = "box-fruit",
        [MealTemplateSlot.BoxSnack] = "box-snack",
        [MealTemplateSlot.SnackFruitOrVeg] = "snack-fruit",
        [MealTemplateSlot.SnackBar] = "snack-bar",
    });

    public static FieldWords<TemplateStatus> Statuses { get; } = new(new Dictionary<TemplateStatus, string>
    {
        [TemplateStatus.Proposed] = "proposed",
        [TemplateStatus.Approved] = "approved",
        [TemplateStatus.Rejected] = "rejected",
    });

    public static FieldWords<TemplateApproval> Approvals { get; } = new(new Dictionary<TemplateApproval, string>
    {
        [TemplateApproval.None] = "none",
        [TemplateApproval.Explicit] = "explicit",
        [TemplateApproval.Silence] = "silence",
    });

    public static MealTemplateKind KindOf(MealTemplateSlot slot) => slot switch
    {
        MealTemplateSlot.WeekdayBreakfast or MealTemplateSlot.WeekendBreakfast => MealTemplateKind.Breakfast,
        MealTemplateSlot.WeekdayLunch or MealTemplateSlot.WeekendLunch => MealTemplateKind.Lunch,
        _ => MealTemplateKind.Lunchbox,
    };
}

/// <summary>
/// One rotating line the household may eat: the plain words the sheet prints for it ("Oats with sliced banana"), the slot it fills,
/// and where it stands with the household (D110). Claude proposes; the household answers on the sheet and the answer moves the status.
/// The id is shaped like a card id (lower-case words joined by dashes, always beginning <c>tpl-</c>) so the taste notes a tick
/// teaches can name the template as a dish without ever being mistaken for a recipe card.
/// <see cref="ToString"/> names none of it.
/// </summary>
public sealed record MealTemplate
{
    public const int MaxWordsLength = 40;

    public const int MaxReasonLength = 300;

    public const string IdPrefix = "tpl-";

    /// <summary>The characters a row's text column holds per line, and how many lines a row has: what the sheet can print.</summary>
    private const int LineChars = 22;

    private const int Lines = 2;

    public MealTemplate(string id, MealTemplateSlot slot, string words)
    {
        Id = id;
        Slot = slot;
        Words = Normalize(words);
        EnsureValid();
    }

    public string Id { get; init; }

    public MealTemplateSlot Slot { get; init; }

    public string Words { get; init; }

    public TemplateStatus Status { get; init; } = TemplateStatus.Proposed;

    public TemplateApproval Approval { get; init; } = TemplateApproval.None;

    /// <summary>The household's own written words when it said no; null otherwise.</summary>
    public string? Reason { get; init; }

    /// <summary>The week of the latest sheet whose answers were applied to this template; null until one was.</summary>
    public DateOnly? FeedbackWeek { get; init; }

    public MealTemplateKind Kind => MealTemplateWords.KindOf(Slot);

    /// <summary>Throws <see cref="ArgumentException"/> naming what is wrong. Run again before writing, since <c>with</c> skips the constructor.</summary>
    public void EnsureValid()
    {
        if (!Id.StartsWith(IdPrefix, StringComparison.Ordinal) || Id.Length > TasteNote.MaxSubjectLength || !IsRecipeShaped(Id))
        {
            throw new ArgumentException($"A template id begins '{IdPrefix}' and is lower-case words joined by dashes, at most {TasteNote.MaxSubjectLength} long.", nameof(Id));
        }

        if (Words.Length == 0 || Words.Length > MaxWordsLength || !Prints(Words))
        {
            throw new ArgumentException($"A template's words are 1 to {MaxWordsLength} characters and must fit a row of the sheet.", nameof(Words));
        }

        if (Reason is { } reason && (string.IsNullOrWhiteSpace(reason) || reason.Length > MaxReasonLength))
        {
            throw new ArgumentException($"A reason is 1 to {MaxReasonLength} characters, or absent.", nameof(Reason));
        }

        var consistent = Status switch
        {
            TemplateStatus.Proposed => Approval == TemplateApproval.None && Reason is null,
            TemplateStatus.Approved => Approval != TemplateApproval.None && Reason is null,
            TemplateStatus.Rejected => Approval == TemplateApproval.None,
            _ => false,
        };
        if (!consistent)
        {
            throw new ArgumentException("A template's approval and reason must agree with its status.", nameof(Status));
        }
    }

    /// <summary>The words as they are compared and kept: single spaces, no blanks around them.</summary>
    public static string Normalize(string? words) =>
        string.Join(' ', (words ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>What two templates' words are compared by: case and spacing do not make a new template.</summary>
    public static string Key(string? words) => Normalize(words).ToLowerInvariant();

    /// <summary>Whether the words fit one row of the sheet: two lines, and no single word wider than a line.</summary>
    public static bool Prints(string words) =>
        PageText.Wrap(words, LineChars).Count <= Lines && words.Split(' ').All(word => word.Length <= LineChars);

    public override string ToString() => nameof(MealTemplate);

    private static bool IsRecipeShaped(string id)
    {
        try
        {
            _ = new RecipeId(id);
            return true;
        }
        catch (ArgumentException)
        {
            // Not shaped like a card id; the caller turns that into its own ArgumentException naming the field.
            return false;
        }
    }
}
