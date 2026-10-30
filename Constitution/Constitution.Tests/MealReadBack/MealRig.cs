using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Recipes;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.HouseholdFile;
using Constitution.Tests.HouseholdHead;
using Constitution.Tests.MealSheet;
using Constitution.Tests.Recipes;
using Storyworthtelling.Household;

namespace Constitution.Tests.MealReadBack;

/// <summary>
/// One invented phone with the household file already on it (Sam, Lee, Robin and Juniper, two dishes in the box) and the hook that
/// records what the pages said. The hook blocks on the file service the way the rest of the head does, so these tests are not async.
/// </summary>
internal sealed class MealRig : IDisposable
{
    public static readonly DateTimeOffset Before = new(2026, 11, 20, 7, 15, 0, TimeSpan.FromHours(-5));
    public static readonly DateTimeOffset After = new(2026, 11, 23, 7, 15, 0, TimeSpan.FromHours(-5));
    public static readonly DateOnly DinnerDay = new(2026, 11, 17);
    public static readonly DateTimeOffset NextMorning = new(2026, 11, 18, 7, 15, 0, TimeSpan.FromHours(-5));

    public MealRig(HouseholdFileRole role = HouseholdFileRole.Owner, bool withHousehold = true)
    {
        Head = new HeadRig(role: role);
        if (withHousehold)
        {
            Hold(RosterData.Household());
            Edit(document => HouseholdRecords.Put(document, RecipeMaps.Recipe, Chicken(), RosterData.At(2000), RosterData.OwnerWriter));
            Edit(document => HouseholdRecords.Put(document, RecipeMaps.Recipe, Noodles(), RosterData.At(2001), RosterData.OwnerWriter));
        }

        Hook = new MealReadBackSettled(Head.Service);
    }

    public HeadRig Head { get; }

    public MealReadBackSettled Hook { get; }

    public static Recipe Chicken() => RecipeData.Plain("sheet-pan-chicken", "Sheet-pan chicken", Cuisine.American, "chicken", "potatoes");

    public static Recipe Noodles() => RecipeData.Plain("buttered-noodles", "Buttered noodles", Cuisine.Italian, "noodles", "butter");

    public void Hold(HouseholdDocument document) => Head.Local.SaveAsync(document, CancellationToken.None).GetAwaiter().GetResult();

    public void Edit(Func<HouseholdDocument, HouseholdDocument> change) => Hold(change(Document()));

    public HouseholdDocument Document() => Head.Service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>The meal answers the file holds for the sheet's week.</summary>
    public IReadOnlyList<MealAnswer> Answers() => MealAnswerRecords.Read(Document(), MealSheetSamples.Sunday).Answers;

    public IReadOnlyList<TasteRating> Ratings() => HouseholdRecords.Read(Document(), TasteMaps.Rating).Items;

    /// <summary>Every record kind the file holds, so a test can say what was NOT written.</summary>
    public IReadOnlyList<string> Kinds() => Document().LiveRecords.Select(record => record.Key.Kind).Distinct().Order().ToList();

    /// <summary>Puts the phone in the state of a file that will not open, as a foreign passphrase does.</summary>
    public void MakeUnreadable()
    {
        Head.Drive.Content = Head.SealedFile(Document(), "invented-someone-else");
        Head.Service.SyncAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(Head.Service.Standing.IsUnreadable);
    }

    public void Dispose() => Head.Dispose();

    // ---- the sheet ----

    public static ExpectedDocument Sheet(string person = "Sam") => ExpectedDocument.ForMealSheet(MealSheetSamples.Model(), person);

    /// <summary>The sheet's dinners page as read: one resolution per weekday, chosen by <paramref name="pick"/> (an untouched line when it says nothing).</summary>
    public static DocumentResult SheetResult(
        ExpectedDocument document, DateTimeOffset readAt, Func<DayOfWeek, InkResolution?>? pick = null, params InkResolution[] extra)
    {
        var rules = document.Pages[0].Rules.OfType<MealItemRule>().ToList();
        var resolutions = new List<InkResolution>();
        foreach (var rule in rules)
        {
            var weekday = Enum.GetValues<DayOfWeek>().Single(day => MealSheetIds.DinnerYes(day) == rule.YesBoxId);
            resolutions.Add(pick?.Invoke(weekday) ?? (readAt >= rule.Deadline
                ? new InkResolution.ApprovedAtDeadline(rule.YesBoxId)
                : new InkResolution.Undecided(rule.YesBoxId)));
        }

        resolutions.AddRange(extra);
        return new DocumentResult(document.Name, document.Kind, document.Day, document.Person, readAt, [new PageResolutions(1, resolutions)]);
    }

    public static string Yes(DayOfWeek day) => MealSheetIds.DinnerYes(day);

    // ---- the recipe ----

    public static ExpectedDocument Recipe(string cook = "Lee") =>
        ExpectedDocument.ForRecipe(Constitution.Tests.RecipeDocuments.RecipeSamples.Timed(), Constitution.Tests.RecipeRating.RatingSamples.Book(), cook, DinnerDay, cook);

    /// <summary>The rating page as read: the named resolutions, and every other row uncircled and the box blank.</summary>
    public static DocumentResult RecipeResult(ExpectedDocument document, DateTimeOffset readAt, params InkResolution[] given)
    {
        var rules = document.Pages[0].Rules;
        var resolutions = new List<InkResolution>(given);
        foreach (var rule in rules.OfType<RatingRule>().Where(rule => given.All(item => item.SubjectId != rule.Group)))
        {
            resolutions.Add(new InkResolution.Unchanged(rule.Group));
        }

        if (given.All(item => item.SubjectId != RecipeIds.DidntCook))
        {
            resolutions.Add(new InkResolution.Skipped(RecipeIds.DidntCook));
        }

        return new DocumentResult(document.Name, document.Kind, document.Day, document.Person, readAt, [new PageResolutions(document.Pages[0].Number, resolutions)]);
    }
}
