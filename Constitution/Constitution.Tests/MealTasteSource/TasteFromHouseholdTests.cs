using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.HouseholdFile;
using Constitution.Tests.MealReadBack;
using Storyworthtelling.Household;

namespace Constitution.Tests.MealTasteSource;

/// <summary>The taste profile the planner is given is built from what the household file keeps: the roster, the box, the ratings and the lasting notes.</summary>
public class TasteFromHouseholdTests
{
    private static readonly PersonId Sam = new("sam");
    private static readonly PersonId Robin = new("robin");
    private static readonly RecipeId Chicken = new("sheet-pan-chicken");
    private static readonly RecipeId Noodles = new("buttered-noodles");
    private static readonly DateOnly Day = new(2026, 11, 17);

    private static long tick = 5000;

    private static void Keep<T>(MealRig rig, RecordMap<T> map, T value)
        where T : notnull =>
        rig.Edit(document => HouseholdRecords.Put(document, map, value, RosterData.At(tick++), RosterData.OwnerWriter));

    private static MealTasteRead.Held Held(MealRig rig) => Assert.IsType<MealTasteRead.Held>(new TasteFromHousehold(rig.Head.Service).Read());

    [Fact]
    public void NothingKeptYetIsNoProfile()
    {
        using var rig = new MealRig();

        Assert.IsType<MealTasteRead.None>(new TasteFromHousehold(rig.Head.Service).Read());
    }

    [Fact]
    public void NoHouseholdAtAllIsNoProfile()
    {
        using var rig = new MealRig(withHousehold: false);

        Assert.IsType<MealTasteRead.None>(new TasteFromHousehold(rig.Head.Service).Read());
    }

    [Fact]
    public void AFileThatWillNotOpenIsUnreadableWithItsOwnSentence()
    {
        using var rig = new MealRig();
        rig.MakeUnreadable();

        var read = Assert.IsType<MealTasteRead.Unreadable>(new TasteFromHousehold(rig.Head.Service).Read());

        Assert.False(string.IsNullOrWhiteSpace(read.Reason));
    }

    [Fact]
    public void RatingsBecomeTheProfileAndTheRosterIsWhoItIsAbout()
    {
        using var rig = new MealRig();
        Keep(rig, TasteMaps.Rating, new TasteRating(Robin, Noodles, Day, TasteVerdict.NeverAgain, null));
        Keep(rig, TasteMaps.Rating, new TasteRating(Sam, Chicken, Day, TasteVerdict.Loved, null));

        var held = Held(rig);

        Assert.Equal([Noodles], held.Profile.For(Robin)!.NeverAgain);
        Assert.Equal([Chicken], held.Profile.For(Sam)!.Loved);
        Assert.Contains(held.People, person => person.Id == Robin && person.Name == "Robin" && person.Role == MemberRole.Child);
        Assert.Equal(4, held.People.Count);
    }

    [Fact]
    public void ALearnedNoteIsInTheProfile()
    {
        using var rig = new MealRig();
        Keep(rig, TasteMaps.Note, new TasteNote(Sam, TasteSubjectKind.Dish, Chicken.Value, TasteStance.Dislikes, TasteOrigin.Learned, Day));

        var taste = Held(rig).Profile.For(Sam)!;

        Assert.True(taste.Dishes[Chicken] < 0);
    }

    [Fact]
    public void ANoteAloneIsEnoughForAProfile()
    {
        using var rig = new MealRig();
        Keep(rig, TasteMaps.Note, new TasteNote(Sam, TasteSubjectKind.Cuisine, "american", TasteStance.Likes, TasteOrigin.Seeded, Day));

        Assert.IsType<MealTasteRead.Held>(new TasteFromHousehold(rig.Head.Service).Read());
    }

    [Fact]
    public void ARecordThisBuildCannotReadIsLeftOutAndTheRestStillCount()
    {
        using var rig = new MealRig();
        Keep(rig, TasteMaps.Rating, new TasteRating(Sam, Chicken, Day, TasteVerdict.Loved, null));
        var sample = new TasteRating(Robin, Noodles, Day.AddDays(1), TasteVerdict.Loved, null);
        var fields = new Dictionary<string, string>(TasteMaps.Rating.FieldsOf(sample)) { ["verdict"] = "a-newer-word" };
        rig.Edit(document => document.With(HouseholdRecord.Write(TasteMaps.Rating.KeyOf(sample), 2, fields, new HlcTimestamp(5, 0), "newer-install")));

        var held = Held(rig);

        Assert.Equal([Chicken], held.Profile.For(Sam)!.Loved);
        Assert.Empty(held.Profile.For(Robin)!.Loved);
    }

    [Fact]
    public void ARatingByNobodyOnTheRosterIsIgnored()
    {
        using var rig = new MealRig();
        Keep(rig, TasteMaps.Rating, new TasteRating(new PersonId("stranger"), Chicken, Day, TasteVerdict.Loved, null));

        var held = Held(rig);

        Assert.Null(held.Profile.For(new PersonId("stranger")));
    }

    [Fact]
    public void WhatTheHouseholdSaidShowsOnTheRatingPage()
    {
        using var rig = new MealRig();
        Keep(rig, TasteMaps.Rating, new TasteRating(Robin, Noodles, Day, TasteVerdict.NeverAgain, null));

        var last = TasteForTheWeek.LastRatings(new TasteFromHousehold(rig.Head.Service).Read());

        Assert.Equal(TasteVerdict.NeverAgain, last.Of(Robin, Noodles));
    }
}
