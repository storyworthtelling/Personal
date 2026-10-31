using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using Constitution.Tests.HouseholdFile;
using Storyworthtelling.Household;

namespace Constitution.Tests.PersonSheet;

/// <summary>Invented household (Sam, Lee, Robin, Juniper; example.com) with things tied to people. Every birthdate is in 1900.</summary>
internal static class SheetData
{
    public static readonly PersonId Sam = new("sam");
    public static readonly PersonId Lee = new("lee");
    public static readonly PersonId Robin = new("robin");
    public static readonly PersonId Juniper = new("juniper");

    public static RecordKey RobinsRating => RatingKey("robin");

    public static RecordKey RobinsNote => new(ConstitutionKinds.TasteNote, RecordIds.Join("robin", "ingredient", "mushroom"));

    public static RecordKey RobinsAnswer => new(ConstitutionKinds.WeekPlan, RecordIds.Join("answer", "robin", "2026-10-25", "monday-dinner"));

    public static RecordKey RatingKey(string person) => new(ConstitutionKinds.Rating, RecordIds.Join(person, "tacos", "2026-10-20"));

    /// <summary>The roster plus a rating for Robin, Sam and Lee, a note and a meal answer for Robin, and a lease held by Lee's install.</summary>
    public static HouseholdDocument Full()
    {
        var document = RosterData.Household();
        document = Rate(document, "robin", 2000);
        document = Rate(document, "sam", 2001);
        document = Rate(document, "lee", 2002);
        document = HouseholdRecords.Put(
            document,
            TasteMaps.Note,
            new TasteNote(Robin, TasteSubjectKind.Ingredient, "mushroom", TasteStance.Dislikes, TasteOrigin.Seeded, new DateOnly(2026, 10, 20)),
            RosterData.At(2003),
            RosterData.OwnerWriter);
        document = document.With(HouseholdRecord.Write(
            RobinsAnswer,
            RecordSchema.Version,
            new Dictionary<string, string> { ["person"] = "robin", ["week"] = "2026-10-25", ["item"] = "monday-dinner", ["verdict"] = "approved", ["read"] = "1" },
            RosterData.At(2004),
            RosterData.PartnerWriter));
        var clock = new HybridLogicalClock();
        var beat = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
        return LeaseWriter.Take(document, clock, "install-lee", RosterRole.Partner, beat, TimeZoneInfo.Utc, LeaseTerms.DefaultLeaseLength);
    }

    /// <summary>Sam as the one adult, Robin as a child.</summary>
    public static HouseholdDocument OneAdult()
    {
        var owner = new RosterEditor(RosterRole.Owner);
        var document = owner.Save(HouseholdDocument.Empty, RosterData.Sam(), RosterData.At(1000), RosterData.OwnerWriter).Document;
        return owner.Save(document, RosterData.Robin(), RosterData.At(1001), RosterData.OwnerWriter).Document;
    }

    public static HlcTimestamp After(HouseholdDocument document, long millis = 1) =>
        new(Math.Max(document.MaxStamp.WallMillis, 5000) + millis, 0);

    public static IReadOnlyList<RosterPerson> People(HouseholdDocument document) =>
        HouseholdRecords.Read(document, RosterMaps.Person).Items;

    private static HouseholdDocument Rate(HouseholdDocument document, string person, long tick) =>
        HouseholdRecords.Put(
            document,
            TasteMaps.Rating,
            new TasteRating(new PersonId(person), new RecipeId("tacos"), new DateOnly(2026, 10, 20), TasteVerdict.Loved),
            RosterData.At(tick),
            RosterData.OwnerWriter);
}
