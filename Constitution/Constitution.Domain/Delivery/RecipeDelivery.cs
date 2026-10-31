using Constitution.Domain.Documents;

namespace Constitution.Domain.Delivery;

/// <summary>What to do about sending tonight's recipe.</summary>
public enum RecipeSend
{
    /// <summary>Not yet: send at <see cref="RecipeSlot.At"/>, as a delayed job.</summary>
    Later,

    /// <summary>Inside the hour already: send now.</summary>
    Now,

    /// <summary>The cook block has begun (or dinner is here): the paper must not arrive under the cook's hand.</summary>
    Never,
}

/// <summary>The decision: what, when (null when never), and how long to delay the job from now.</summary>
public sealed record RecipeSlot(RecipeSend Send, DateTimeOffset? At, TimeSpan Delay);

/// <summary>Which tablets get the recipe and when.</summary>
public sealed record RecipeDeliveryPlan(IReadOnlyList<string> Tablets, RecipeSlot Slot);

/// <summary>
/// C-2.5.5: the recipe reaches the cook's tablet, and only the cook's, an hour before the cook block (before the morning
/// block for a slow-cooker meal, whose first step is the morning one). Pure: the instant and the owner's zone are passed in.
///
/// <para><b>An hour is an hour of elapsed time</b>, so across a clock change it is the instant sixty minutes earlier, not the
/// same wall-clock reading less one. If that moment has already passed the recipe goes at once, unless the block has begun
/// (mirroring <see cref="Workouts.Pipeline.RedeliveryRules"/>: a page never appears once its slot has started), and in no
/// case once dinner is on the table. This is the decision only; <c>MealDocumentsStep</c> asks it on every pass of the meal worker (the recipes-only pass included, D120), and the worker's half-hourly passes are the delayed job.</para>
/// </summary>
public static class RecipeDelivery
{
    public static readonly TimeSpan Lead = TimeSpan.FromHours(1);

    public static RecipeSlot Slot(DateTimeOffset cookBlockStart, DateTimeOffset now, DateTimeOffset? dinnerAt = null)
    {
        if (now >= cookBlockStart || dinnerAt is { } dinner && now >= dinner)
        {
            return new RecipeSlot(RecipeSend.Never, null, TimeSpan.Zero);
        }

        var at = cookBlockStart - Lead;
        return now >= at
            ? new RecipeSlot(RecipeSend.Now, now, TimeSpan.Zero)
            : new RecipeSlot(RecipeSend.Later, at, at - now);
    }

    /// <summary>
    /// When the cook block begins on the owner's clock: the first step's time (the morning one for a slow-cooker meal),
    /// worked back from dinner exactly as the document prints it. A wall-clock reading that does not exist or happens twice on
    /// a clock-change day takes the zone's standard offset.
    /// </summary>
    public static DateTimeOffset CookBlockStart(RecipeDocumentModel model, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(zone);
        var first = model.Steps is null ? model.StartAt : RecipeSchedule.Plan(model.Steps, model.DinnerAt, model.StartAt, null).Rows[0].At;
        return On(first, zone);
    }

    /// <summary>The tablet is the cook's alone, and the cook must be one of the adults whose tablet there is.</summary>
    public static RecipeDeliveryPlan Plan(string cook, IReadOnlyList<string> adults, RecipeDocumentModel model, TimeZoneInfo zone, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(adults);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(zone);
        if (string.IsNullOrWhiteSpace(cook))
        {
            throw new ArgumentException("A recipe goes to the cook's tablet; there is no cook named.", nameof(cook));
        }

        var tablet = adults.FirstOrDefault(adult => string.Equals(adult?.Trim(), cook.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"'{cook.Trim()}' is not one of the adults with a tablet ({string.Join(", ", adults)}), so the recipe has nowhere to go.", nameof(cook));
        return new RecipeDeliveryPlan([tablet.Trim()], Slot(CookBlockStart(model, zone), now, On(model.DinnerAt, zone)));
    }

    private static DateTimeOffset On(DateTime wall, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }
}
