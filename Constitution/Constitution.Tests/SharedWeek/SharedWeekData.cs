using Constitution.Domain.Documents;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Tests.MealWeekDocuments;
using Storyworthtelling.Household;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.SharedWeek;

/// <summary>Invented plans for the other phone's week. Sunday 25 October 2026; Sunday's dinner is Lee's, with the block from 5:15 pm.</summary>
internal static class SharedWeekData
{
    public const string OwnerPhone = "owner-install";
    public const string PartnerPhone = "partner-install";

    public static readonly DateTimeOffset Deadline = Edt(10, 25, 8);

    public static MealSheetDailyMeals Daily() => new(
        [new MealSheetLine("Sam", "Yogurt and fruit"), new MealSheetLine("Lee", "Toast and eggs")],
        [new MealSheetLine("Kids", "Quesadilla")],
        "Pancakes",
        "Soup and bread",
        new MealSheetLunchbox("Turkey sandwich", "Apple", "Crackers", "Carrot sticks", "Granola bar"));

    public static StoredMealWeek Stored(DateTimeOffset? plannedAt = null) =>
        new(DocsData.Week(), plannedAt ?? Edt(10, 21, 12), DocsData.Cards().Take(3).Select(card => new StoredDish(card.Id, card.Title, card.Id.Value == "lentil-soup", card.Id.Value == "veggie-fried-rice")).ToList());

    public static SharedWeekPlan Plan(IReadOnlyList<string>? extra = null, MealSheetDailyMeals? daily = null, DateTimeOffset? plannedAt = null) =>
        new(Stored(plannedAt), Deadline, extra ?? [], daily);

    public static HlcTimestamp At(long millis) => new(millis, 0);

    public static HouseholdDocument Put(SharedWeekPlan plan, long millis = 10, string writer = OwnerPhone, HouseholdDocument? into = null) =>
        HouseholdRecords.Put(into ?? HouseholdDocument.Empty, WeekPlanMaps.WeekPlan, plan, At(millis), writer);

    public static HouseholdRecord Record(HouseholdDocument document) => Assert.Single(document.LiveRecords);

    /// <summary>The document with one field of the week's record replaced, as another build's writer might have left it.</summary>
    public static HouseholdDocument WithField(HouseholdDocument document, string name, string value, long millis = 20)
    {
        var record = Record(document);
        var changes = new Dictionary<string, string> { [name] = value };
        return document.With(record.Edit(changes, record.SchemaVersion, At(millis), record.Writer));
    }
}
