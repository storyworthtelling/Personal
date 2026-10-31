namespace Constitution.Domain.Meals.Pipeline;

/// <summary>The shopping-list lines a week's denials add, or the reason they could not be read. Lines are empty when the reason is set.</summary>
public sealed record MealExtraLinesRead(IReadOnlyList<string> Lines, string? UnreadableBecause);

/// <summary>
/// Reads the decision record for the lines a denied evening adds to the shopping list (D111, D112). Failing open on purpose: the list is a convenience built from
/// the record, so a record that cannot be read gives no lines and says why, rather than stopping a sheet or a share. Never an empty answer that hides the reason.
/// </summary>
public static class MealExtraLines
{
    public static MealExtraLinesRead Read(IMealDecisionRecords? records, DateOnly week)
    {
        if (records is null)
        {
            return new MealExtraLinesRead([], null);
        }

        switch (records.Load())
        {
            case MealDecisionRecordRead.Held held:
                return new MealExtraLinesRead(held.Record.For(week)?.ExtraLines ?? [], null);
            case MealDecisionRecordRead.NothingYet:
                return new MealExtraLinesRead([], null);
            case MealDecisionRecordRead.Unreadable unreadable:
                return new MealExtraLinesRead([], unreadable.Reason);
            default:
                throw new InvalidOperationException("The decision record came back as neither a record, nothing, nor a reason.");
        }
    }
}
