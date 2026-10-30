using Constitution.Domain.Household;
using Constitution.Domain.Documents;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Recipes;
using Constitution.Infrastructure.Household;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.MealWeekRun;
using Constitution.Tests.TemplatesInTheRun;
using Constitution.Tests.MealTaste;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealAnswers.MealAnswerData;
using Step = Constitution.Infrastructure.Meals.MealDeadlineStep;

namespace Constitution.Tests.MealDeadlineStep;

/// <summary>
/// The deadline step with every seam faked over an invented week (Sunday 25 October 2026, Sam and Lee). The week is planned by the real weekly meal run on the
/// Wednesday before, so the kept week is a real one; the clock then moves to <see cref="Now"/>.
/// </summary>
internal sealed class DeadlineRig
{
    public const string TickedLine = "box-fruit-yes";
    public const string TickedTemplate = "tpl-box-fruit-oats";

    private DeadlineRig()
    {
    }

    /// <summary>The Sunday afternoon: the deadline (8 am) has passed.</summary>
    public DateTimeOffset Now { get; set; } = Edt(10, 25, 15);

    public bool MayPlan { get; set; } = true;

    public int LeaseAsks { get; private set; }

    public MealWeekRig Planning { get; private init; } = null!;

    public MemoryDecisions Decisions { get; } = new();

    public FakeAnswers Answers { get; } = new();

    public FakeTemplateStore Library { get; private init; } = null!;

    public FakeNotes Notes { get; } = new();

    public FakeTasteJudge? Judge { get; set; }

    public bool JudgeAvailable { get; set; }

    public RecordingLog Log { get; } = new();

    public MemoryRecords Weeks => Planning.Records;

    public MealDeadlineSeams Seams => new(
        _ =>
        {
            LeaseAsks++;
            return Task.FromResult(new PlanningStanding(MayPlan, null, MayPlan ? "This phone plans this week." : "The other phone holds this week's planning."));
        },
        Weeks,
        Decisions,
        Answers,
        Library,
        Notes,
        Judge is not null ? Judge : new UnavailableTasteJudge(),
        Log);

    /// <summary>A planned week kept in the records, and a library of proposed templates one of which the sheet printed.</summary>
    public static async Task<DeadlineRig> PlannedAsync()
    {
        var planning = new MealWeekRig();
        await planning.PlannedAsync();
        var proposed = new MealTemplate(TickedTemplate, MealTemplateSlot.BoxFruit, "oats for box fruit");
        var library = new TemplateLibrary([proposed], [new TemplatePrint(Week, TickedLine, TickedTemplate)]);
        return new DeadlineRig { Planning = planning, Library = new FakeTemplateStore(library) };
    }

    public StoredMealWeek Kept => Weeks.Held().For(Week)!;

    /// <summary>The first cooked dinner of the kept week: its weekday's line and its card.</summary>
    public (string Line, RecipeId Card, DateOnly Day) CookedDinner(int skip = 0)
    {
        var dinner = Kept.Week.Dinners.Where(item => item.Kind == DinnerKind.Cooked).Skip(skip).First();
        return (MealSheetIds.DinnerYes(dinner.Day.DayOfWeek), dinner.Recipe!.Value, dinner.Day);
    }

    public Task<MealDeadlineOutcome> RunAsync() => Step.RunAsync(new MealDeadlineInputs(Now, Rules()), Seams, CancellationToken.None);

    public async Task<MealDeadlineReport> DecidedAsync()
    {
        var outcome = await RunAsync();
        return Assert.IsType<MealDeadlineOutcome.Decided>(outcome).Report;
    }

    public void AssertNothingTouched()
    {
        Assert.Equal(0, Answers.Reads);
        Assert.Equal(0, Decisions.Saves);
        Assert.Equal(0, Notes.Saves);
        Assert.Equal(0, Library.Saves);
    }
}

internal sealed class MemoryDecisions : IMealDecisionRecords
{
    public string? Json { get; set; }

    public int Loads { get; private set; }

    public int Saves { get; private set; }

    public MealDecisionRecordRead Load()
    {
        Loads++;
        return MealDecisionRecordCodec.Decode(Json);
    }

    public void Save(MealDecisionRecord record)
    {
        Saves++;
        Json = MealDecisionRecordCodec.Encode(record);
    }

    public MealDecisionRecord Held() => Assert.IsType<MealDecisionRecordRead.Held>(MealDecisionRecordCodec.Decode(Json)).Record;
}

internal sealed class FakeAnswers : IMealAnswerSource
{
    public List<MealAnswer> All { get; } = [];

    public int UnreadableRecords { get; set; }

    public string? Cannot { get; set; }

    public int Reads { get; private set; }

    public MealAnswersRead Read(DateOnly week)
    {
        Reads++;
        return Cannot is null
            ? new MealAnswersRead.Held(All.Where(answer => answer.Week == week).ToList(), UnreadableRecords)
            : new MealAnswersRead.Unreadable(Cannot);
    }
}

internal sealed class FakeNotes : IMealTasteNotes
{
    public List<TasteNote> Kept { get; } = [];

    public string? RefuseWith { get; set; }

    public int Saves { get; private set; }

    public MealNotesSave Save(IReadOnlyList<TasteNote> notes)
    {
        Saves++;
        if (RefuseWith is not null)
        {
            return new MealNotesSave.Refused(RefuseWith);
        }

        foreach (var note in notes)
        {
            Kept.RemoveAll(existing => existing.Person == note.Person && existing.Kind == note.Kind && existing.Subject == note.Subject);
            Kept.Add(note);
        }

        return new MealNotesSave.Saved(notes.Count);
    }
}
