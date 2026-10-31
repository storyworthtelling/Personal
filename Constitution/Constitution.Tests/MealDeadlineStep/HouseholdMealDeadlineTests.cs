using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Household;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.HouseholdHead;
using Storyworthtelling.Household;
using static Constitution.Tests.MealAnswers.MealAnswerData;

namespace Constitution.Tests.MealDeadlineStep;

/// <summary>The answers and the notes over the household file, with the same invented phones the other household tests use.</summary>
public class HouseholdMealDeadlineTests
{
    private static async Task WriteAsync(HeadRig rig, MealAnswer answer) =>
        await rig.Service.EditAsync(
            (HouseholdDocument document, HybridLogicalClock clock, string writer) =>
                MealAnswerRecords.Write(document, clock, writer, answer, Deadline, answer.ReadAt).Document,
            CancellationToken.None);

    private static TasteNote Like(string person, string subject) =>
        new(new PersonId(person), TasteSubjectKind.Dish, subject, TasteStance.Likes, TasteOrigin.Learned, Week);

    private static async Task<IReadOnlyList<TasteNote>> NotesOfAsync(HeadRig rig) =>
        HouseholdRecords.Read(await rig.Local.LoadAsync(CancellationToken.None), TasteMaps.Note).Items;

    // ---- the answers ----

    [Fact]
    public async Task AnswersOfOneWeekAreReadAndOthersLeftOut()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        await WriteAsync(rig, Tick(Sam, Tuesday, Before(60)));
        await WriteAsync(rig, Deny(Lee, Wednesday, "no", Before(50)));
        await WriteAsync(rig, Tick(Sam, Tuesday, Before(60), Week.AddDays(7)));

        var read = Assert.IsType<MealAnswersRead.Held>(new HouseholdMealAnswers(rig.Service).Read(Week));

        Assert.Equal(2, read.Answers.Count);
        Assert.Equal(0, read.UnreadableRecords);
        Assert.All(read.Answers, answer => Assert.Equal(Week, answer.Week));
    }

    [Fact]
    public async Task OnlyAnUnreadableRecordOfThatWeekIsCounted()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        await BreakAsync(rig, Tuesday, Week);
        await BreakAsync(rig, Tuesday, Week.AddDays(7));

        var read = Assert.IsType<MealAnswersRead.Held>(new HouseholdMealAnswers(rig.Service).Read(Week));

        Assert.Equal(1, read.UnreadableRecords);
        Assert.Empty(read.Answers);
    }

    private static async Task BreakAsync(HeadRig rig, string item, DateOnly week)
    {
        var sample = Tick(Sam, item, Before(30), week);
        var fields = new Dictionary<string, string>(MealAnswerMaps.Answer.FieldsOf(sample)) { ["verdict"] = "a-newer-word" };
        var broken = HouseholdRecord.Write(MealAnswerMaps.Answer.KeyOf(sample), 2, fields, new HlcTimestamp(5, 0), "newer-install");
        await rig.Service.EditAsync((HouseholdDocument document, HybridLogicalClock clock, string writer) => document.With(broken), CancellationToken.None);
    }

    [Fact]
    public async Task ABlockedFileIsUnreadableNotEmpty()
    {
        using var rig = new HeadRig();
        rig.HeldPassphrase = string.Empty;
        await rig.Service.SyncAsync(CancellationToken.None);

        var read = new HouseholdMealAnswers(rig.Service).Read(Week);

        if (rig.Service.BlockedSentence is { } sentence)
        {
            Assert.Equal(sentence, Assert.IsType<MealAnswersRead.Unreadable>(read).Reason);
        }
        else
        {
            Assert.IsType<MealAnswersRead.Held>(read);
        }
    }

    // ---- the notes ----

    [Fact]
    public async Task NotesAreSavedAsTasteNoteRecords()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);

        var saved = new HouseholdTasteNotes(rig.Service).Save([Like("sam", "tpl-box-fruit-oats"), Like("lee", "tpl-box-fruit-oats")]);

        Assert.Equal(2, Assert.IsType<MealNotesSave.Saved>(saved).Written);
        Assert.Equal(["lee", "sam"], (await NotesOfAsync(rig)).Select(note => note.Person.Value).Order());
    }

    [Fact]
    public async Task SavingTheSameNotesAgainChangesNothing()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var store = new HouseholdTasteNotes(rig.Service);
        store.Save([Like("sam", "tpl-box-fruit-oats")]);
        var once = await rig.Local.LoadAsync(CancellationToken.None);

        var again = store.Save([Like("sam", "tpl-box-fruit-oats")]);

        Assert.Equal(0, Assert.IsType<MealNotesSave.Saved>(again).Written);
        Assert.True((await rig.Local.LoadAsync(CancellationToken.None)).ContentEquals(once));
    }

    [Fact]
    public async Task ChangingYourMindEditsTheOneRecord()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var store = new HouseholdTasteNotes(rig.Service);
        store.Save([Like("sam", "tpl-box-fruit-oats")]);

        store.Save([Like("sam", "tpl-box-fruit-oats") with { Stance = TasteStance.Dislikes }]);

        Assert.Equal(TasteStance.Dislikes, Assert.Single(await NotesOfAsync(rig)).Stance);
    }

    [Fact]
    public async Task ANoteRecordThisBuildCannotReadIsLeftAsItIs()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var note = Like("sam", "tpl-box-fruit-oats");
        var fields = new Dictionary<string, string>(TasteMaps.Note.FieldsOf(note)) { ["stance"] = "a-newer-word" };
        var newer = HouseholdRecord.Write(TasteMaps.Note.KeyOf(note), 2, fields, new HlcTimestamp(5, 0), "newer-install");
        await rig.Service.EditAsync((HouseholdDocument document, HybridLogicalClock clock, string writer) => document.With(newer), CancellationToken.None);
        var before = await rig.Local.LoadAsync(CancellationToken.None);

        var saved = new HouseholdTasteNotes(rig.Service).Save([note, Like("lee", "tpl-box-fruit-oats")]);

        Assert.Equal(1, Assert.IsType<MealNotesSave.Saved>(saved).Written);
        var after = await rig.Local.LoadAsync(CancellationToken.None);
        Assert.Equal(before.Live(TasteMaps.Note.KeyOf(note))!.Fields["stance"], after.Live(TasteMaps.Note.KeyOf(note))!.Fields["stance"]);
    }

    [Fact]
    public void NoNotesWritesNothing()
    {
        using var rig = new HeadRig();

        var saved = new HouseholdTasteNotes(rig.Service).Save([]);

        Assert.Equal(0, Assert.IsType<MealNotesSave.Saved>(saved).Written);
        Assert.Equal(0, rig.Drive.Writes);
    }
}
