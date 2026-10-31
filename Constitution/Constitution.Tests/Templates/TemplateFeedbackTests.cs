using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;
using static Constitution.Tests.MealAnswers.MealAnswerData;
using static Constitution.Tests.Templates.TemplateData;

namespace Constitution.Tests.Templates;

/// <summary>
/// What the sheet's answers do to the library and to the taste. A tick is a statement and teaches a like; silence at the deadline
/// approves the line (D103) and teaches nothing; a written no rejects the line and its words go to the judge as every denial's do.
/// </summary>
public class TemplateFeedbackTests
{
    private const string Item = "weekend-breakfast-yes";
    private const string OtherItem = "box-fruit-yes";
    private static readonly DateOnly Day = new(2026, 10, 25);

    private static MealTemplate Fruit => Proposed("tpl-weekend-breakfast-fruit", MealTemplateSlot.WeekendBreakfast, "Fruit salad and toast");

    private static TemplateLibrary Printed(params MealTemplate[] templates) =>
        new(templates, templates.Select(template => new TemplatePrint(Week, template.Slot == MealTemplateSlot.BoxFruit ? OtherItem : Item, template.Id)).ToList());

    private static MealDecision DecisionFor(TemplateLibrary library, DateTimeOffset now, MealAnswer[] answers) =>
        MealDeadline.Decide(answers, Deadline, now, library.Prints.Select(print => print.ItemId));

    private static TemplateFeedbackResult Run(TemplateLibrary library, DateTimeOffset now, params MealAnswer[] answers) =>
        TemplateFeedback.From(DecisionFor(library, now, answers), answers, library, Day, Week);

    private static MealTemplate Only(TemplateFeedbackResult result, string id) => result.Library.Find(id)!;

    [Fact]
    public void ATickApprovesAtTheDeadlineAndTeachesALike()
    {
        var result = Run(Printed(Fruit), After(5), Tick(Sam, Item, Before(60)));

        var template = Only(result, Fruit.Id);
        Assert.Equal(TemplateStatus.Approved, template.Status);
        Assert.Equal(TemplateApproval.Explicit, template.Approval);
        var like = Assert.Single(result.Likes);
        Assert.Equal(new TasteNote(Sam, TasteSubjectKind.Dish, Fruit.Id, TasteStance.Likes, TasteOrigin.Learned, Day), like);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void EveryAdultWhoTickedLikesIt()
    {
        var result = Run(Printed(Fruit), After(5), Tick(Sam, Item, Before(60)), Tick(Lee, Item, Before(30)));

        Assert.Equal([Lee, Sam], result.Likes.Select(note => note.Person).OrderBy(person => person.Value, StringComparer.Ordinal));
    }

    [Fact]
    public void AdultsWhoTickedLateAreNotCountedAsLiking()
    {
        var result = Run(Printed(Fruit), After(60), Tick(Sam, Item, Before(60)), Tick(Lee, Item, After(30)));

        Assert.Equal([Sam], result.Likes.Select(note => note.Person));
    }

    [Fact]
    public void ATickBeforeTheDeadlineChangesNothingYet()
    {
        var library = Printed(Fruit);
        var result = Run(library, Before(10), Tick(Sam, Item, Before(60)));

        Assert.Equal(TemplateStatus.Proposed, Only(result, Fruit.Id).Status);
        Assert.Empty(result.Likes);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void SilenceApprovesButTeachesNothing()
    {
        var result = Run(Printed(Fruit), After(5));

        var template = Only(result, Fruit.Id);
        Assert.Equal(TemplateStatus.Approved, template.Status);
        Assert.Equal(TemplateApproval.Silence, template.Approval);
        Assert.Empty(result.Likes);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void ALateTickDoesNotMakeSilenceExplicit()
    {
        var result = Run(Printed(Fruit), After(60), Tick(Sam, Item, After(30)));

        Assert.Equal(TemplateApproval.Silence, Only(result, Fruit.Id).Approval);
        Assert.Empty(result.Likes);
    }

    [Fact]
    public void ANoWithWordsRejectsAndSendsTheWordsToTheJudge()
    {
        var result = Run(Printed(Fruit), Before(10), Deny(Lee, Item, "Robin finds it too sweet", Before(40)));

        var template = Only(result, Fruit.Id);
        Assert.Equal(TemplateStatus.Rejected, template.Status);
        Assert.Equal("Robin finds it too sweet", template.Reason);
        Assert.Empty(result.Likes);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(new TasteEvidence(Lee, TasteEvidenceKind.Denial, "Robin finds it too sweet", new RecipeId(Fruit.Id), Day), evidence);
    }

    [Fact]
    public void ANoWithoutWordsRejectsAndHasNothingToJudge()
    {
        var result = Run(Printed(Fruit), Before(10), Deny(Lee, Item, null, Before(40)));

        Assert.Equal(TemplateStatus.Rejected, Only(result, Fruit.Id).Status);
        Assert.Null(Only(result, Fruit.Id).Reason);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void TwoNosKeepBothReasonsAndBothEvidence()
    {
        var result = Run(Printed(Fruit), Before(10), Deny(Lee, Item, "too sweet", Before(40)), Deny(Sam, Item, "no fruit at breakfast", Before(20)));

        Assert.Equal(2, result.Evidence.Count);
        Assert.Contains("too sweet", Only(result, Fruit.Id).Reason);
        Assert.Contains("no fruit at breakfast", Only(result, Fruit.Id).Reason);
    }

    [Fact]
    public void ANoBeatsATick()
    {
        var result = Run(Printed(Fruit), After(5), Tick(Sam, Item, Before(60)), Deny(Lee, Item, "no", Before(30)));

        Assert.Equal(TemplateStatus.Rejected, Only(result, Fruit.Id).Status);
        Assert.Empty(result.Likes);
    }

    [Fact]
    public void ANoRejectsATemplateThatWasApproved()
    {
        var apple = Approved("tpl-box-fruit-apple", MealTemplateSlot.BoxFruit, "Apple slices");

        var result = Run(Printed(apple), Before(10), Deny(Lee, OtherItem, "went off apples", Before(40)));

        Assert.Equal(TemplateStatus.Rejected, Only(result, apple.Id).Status);
    }

    [Fact]
    public void ATickOnASilentlyApprovedTemplateMakesItExplicit()
    {
        var apple = Approved("tpl-box-fruit-apple", MealTemplateSlot.BoxFruit, "Apple slices", TemplateApproval.Silence);

        var result = Run(Printed(apple), After(5), Tick(Lee, OtherItem, Before(40)));

        Assert.Equal(TemplateApproval.Explicit, Only(result, apple.Id).Approval);
        Assert.Single(result.Likes);
    }

    [Fact]
    public void SilenceNeverDowngradesATick()
    {
        var apple = Approved("tpl-box-fruit-apple", MealTemplateSlot.BoxFruit, "Apple slices");

        var result = Run(Printed(apple), After(5));

        Assert.Equal(TemplateApproval.Explicit, Only(result, apple.Id).Approval);
        Assert.Empty(result.Likes);
    }

    [Fact]
    public void ARejectedTemplateStaysRejected()
    {
        var melon = Rejected("tpl-box-fruit-melon", MealTemplateSlot.BoxFruit, "Cold melon", "too wet");

        var result = Run(Printed(melon), After(5), Tick(Sam, OtherItem, Before(40)));

        Assert.Equal(TemplateStatus.Rejected, Only(result, melon.Id).Status);
        Assert.Empty(result.Likes);
    }

    [Fact]
    public void AnotherWeeksPrintIsNotThisWeeksAnswer()
    {
        var library = new TemplateLibrary([Fruit], [new TemplatePrint(WeekAfter(-1), Item, Fruit.Id)]);

        var result = Run(library, After(5), Tick(Sam, Item, Before(40)));

        Assert.Equal(TemplateStatus.Proposed, Only(result, Fruit.Id).Status);
        Assert.Empty(result.Likes);
    }

    [Fact]
    public void ALineThatWasNotPrintedIsLeftAlone()
    {
        var result = Run(Printed(Fruit), After(5), Tick(Sam, "dinner-tue-yes", Before(40)));

        Assert.Equal(TemplateApproval.Silence, Only(result, Fruit.Id).Approval);
        Assert.Empty(result.Likes);
    }

    [Fact]
    public void TheSameAnswersTwiceChangeNothingTheSecondTime()
    {
        var answers = new[] { Tick(Sam, Item, Before(60)), Deny(Lee, OtherItem, "too wet", Before(40)) };
        var melon = Approved("tpl-box-fruit-melon", MealTemplateSlot.BoxFruit, "Cold melon");
        var library = Printed(Fruit, melon);

        var first = Run(library, After(5), answers);
        var second = TemplateFeedback.From(DecisionFor(library, After(5), answers), answers, first.Library, Day, Week);

        Assert.NotEmpty(first.Likes);
        Assert.NotEmpty(first.Evidence);
        Assert.Empty(second.Likes);
        Assert.Empty(second.Evidence);
        Assert.Equal(first.Library.Templates, second.Library.Templates);
        // And it is pure: the original library gives the original result again.
        Assert.Equal(first.Likes, Run(library, After(5), answers).Likes);
    }

    [Fact]
    public void ThePrintedWeekIsRememberedOnTheTemplate()
    {
        var result = Run(Printed(Fruit), After(5));

        Assert.Equal(Week, Only(result, Fruit.Id).FeedbackWeek);
    }

    [Fact]
    public void TheJudgeLearnsADishLessonAboutTheTemplate()
    {
        var result = Run(Printed(Fruit), Before(10), Deny(Lee, Item, "Robin finds it too sweet", Before(40)));
        var judge = new FixedJudge(new TasteJudgement.Lasting([new TasteLesson(TasteSubjectKind.Dish, Fruit.Id, TasteStance.Dislikes)]));

        var learned = TasteLearner.LearnAsync(result.Evidence, judge, CancellationToken.None).GetAwaiter().GetResult();

        var note = Assert.Single(learned.Notes);
        Assert.Equal((TasteSubjectKind.Dish, TasteStance.Dislikes, Fruit.Id), (note.Kind, note.Stance, note.Subject));
    }

    private sealed class FixedJudge(TasteJudgement judgement) : ITasteJudge
    {
        public Task<TasteJudgement> JudgeAsync(TasteEvidence evidence, CancellationToken cancellation) => Task.FromResult(judgement);
    }

    [Fact]
    public void ADecisionForNoWeekChangesNothing()
    {
        var library = Printed(Fruit);
        var result = TemplateFeedback.From(MealDeadline.Decide([], Deadline, After(5)), [], library, Day);

        Assert.Equal(library.Templates, result.Library.Templates);
    }
}
