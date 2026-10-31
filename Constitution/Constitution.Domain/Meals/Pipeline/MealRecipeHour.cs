using Constitution.Domain.Calendar;
using Constitution.Domain.Delivery;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// When a recipe is in its hour (D120): from sixty minutes before its cook block until the block begins, the same window <see cref="RecipeDelivery"/> and
/// <see cref="MealRedeliveryRules"/> apply when the page is actually sent. Pure: the moment, the kept weeks and a lookup of what is already on the tablet are handed in.
///
/// <para><b>One rule, two readers.</b> The pass asks it cheaply (<see cref="MealPassPlan"/>, from the kept weeks and the record of deliveries, both small local files)
/// and the recipes run asks it again to choose the weeks it works on (<c>MealRecipeHourRun</c>), so the two cannot disagree about whether a page is waiting.</para>
///
/// <para><b>Whose.</b> Only a dinner the adult whose tablet this phone has will cook is looked at (D85: each phone serves its own tablet); the partner's recipes are hers.
/// A page whose delivery is already recorded is not due again; the record is looked up only when an hour is open, so a quiet pass reads nothing more.</para>
///
/// <para><b>The window is the planned block.</b> It is measured from the cook block's start as the week was kept. The documents step measures from the earlier of that and the
/// card's own first step, so a card whose steps run longer than its block opens its hour earlier than this look does (D120 records that gap).</para>
/// </summary>
public static class MealRecipeHour
{
    /// <summary>How long before the block a recipe goes out.</summary>
    public static readonly TimeSpan Hour = RecipeDelivery.Lead;

    /// <summary>Whether <paramref name="now"/> is inside this dinner's hour: from <see cref="Hour"/> before the cook block starts until it starts. Only a cooked dinner has one.</summary>
    public static bool InHour(DinnerChoice dinner, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(dinner);

        return dinner.Kind == DinnerKind.Cooked && dinner.CookStart is { } start && now >= start - Hour && now < start;
    }

    /// <summary>
    /// The Sundays of the kept weeks that have a recipe for <paramref name="person"/> in its hour and no recorded delivery, in week order. Weeks that are not there or cannot be
    /// read have none (the deadline step is what reports an unreadable record), and no person means no tablet to send to.
    /// </summary>
    public static IReadOnlyList<DateOnly> DueWeeks(MealWeekRecordRead weeks, DateTimeOffset now, string? person, Func<string, DeliveredMealDocument?> delivered)
    {
        ArgumentNullException.ThrowIfNull(weeks);
        ArgumentNullException.ThrowIfNull(delivered);

        if (string.IsNullOrWhiteSpace(person) || weeks is not MealWeekRecordRead.Held held)
        {
            return [];
        }

        var due = new List<DateOnly>();
        foreach (var week in held.Record.Weeks.Where(item => item.Week.IsPlanned).OrderBy(item => item.WeekStart))
        {
            var waiting = week.Week.Dinners
                .Where(dinner => InHour(dinner, now) && string.Equals(dinner.Cook!.Trim(), person.Trim(), StringComparison.OrdinalIgnoreCase))
                .Any(dinner => delivered(DeliveredName.For(DeliveryKind.Recipe, dinner.Day, dinner.Cook!)) is null);
            if (waiting)
            {
                due.Add(week.WeekStart);
            }
        }

        return due;
    }

    /// <summary>The adult whose tablet this phone has: the first adult the setup names, by the name the rules know them by; null when none is named or the name is not an adult.</summary>
    public static string? TabletPerson(PlanningRules rules, IReadOnlyList<string> deliverFor)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(deliverFor);

        return deliverFor.Count == 0 ? null : rules.FindAdult(deliverFor[0])?.Name;
    }
}

/// <summary>Why the recipes run sent nothing. A closed list: the trail prints the words.</summary>
public enum MealRecipesStoppedBecause
{
    /// <summary>There is no household setup on this phone, so there are no rules and no tablet to name.</summary>
    NotConfigured,

    /// <summary>The kept weeks are there and cannot be read.</summary>
    WeeksUnreadable,

    /// <summary>The recipe box cannot be read now (no household file yet, or one that will not open).</summary>
    RecipesUnavailable,

    /// <summary>The taste profile is there and cannot be read. Read as empty it would print a rating underline that says nobody has rated anything.</summary>
    TasteUnreadable,
}

/// <summary>What the documents step said about one kept week.</summary>
public sealed record MealRecipesWeek(DateOnly Week, MealDocumentsResult Documents);

/// <summary>What the recipes run did. A closed set; a stop is never a throw.</summary>
public abstract record MealRecipesOutcome
{
    private MealRecipesOutcome()
    {
    }

    /// <summary>No kept week has a recipe in its hour that is not already on the tablet. The sentence is fixed words.</summary>
    public sealed record Nothing(string Sentence) : MealRecipesOutcome;

    /// <summary>The run could not look. The sentence is fixed words and quotes nothing the household wrote.</summary>
    public sealed record Stopped(MealRecipesStoppedBecause Because, string Sentence) : MealRecipesOutcome;

    /// <summary>The documents step ran for each due week. A page the tablet refused is in the step's own result, not a throw.</summary>
    public sealed record Done(IReadOnlyList<MealRecipesWeek> Weeks) : MealRecipesOutcome;
}

/// <summary>
/// The recipes run's own words in the Body trail: fixed phrases, never a title, a name or a date. The documents step writes its own lines for what it did.
/// Every word is in the trail vocabulary (<c>TrailVocabularyTests</c> reads the literals returned below), all lower case.
/// </summary>
public static class MealRecipesTrail
{
    public const string NoneDueSentence = "No kept week has a recipe in its hour that is not already on the tablet.";

    /// <summary>The line for an outcome; empty for <see cref="MealRecipesOutcome.Done"/>, which the documents step has already written about.</summary>
    public static string LogPhrase(MealRecipesOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        switch (outcome)
        {
            case MealRecipesOutcome.Nothing:
                return "meal recipes: none due";
            case MealRecipesOutcome.Stopped stopped:
                return LogPhrase(stopped.Because);
            case MealRecipesOutcome.Done:
                return string.Empty;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "This outcome has no trail phrase; add one here.");
        }
    }

    private static string LogPhrase(MealRecipesStoppedBecause because)
    {
        switch (because)
        {
            case MealRecipesStoppedBecause.NotConfigured:
                return "meal recipes not delivered, not configured";
            case MealRecipesStoppedBecause.WeeksUnreadable:
                return "meal recipes not delivered, weeks unreadable";
            case MealRecipesStoppedBecause.RecipesUnavailable:
                return "meal recipes not delivered, recipe box unreadable";
            case MealRecipesStoppedBecause.TasteUnreadable:
                return "meal recipes not delivered, taste profile unreadable";
            default:
                throw new ArgumentOutOfRangeException(nameof(because), because, "This reason has no trail phrase; add one here.");
        }
    }
}
