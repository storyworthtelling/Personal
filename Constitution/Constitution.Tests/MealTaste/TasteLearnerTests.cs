using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;
using static Constitution.Tests.MealTaste.TasteData;

namespace Constitution.Tests.MealTaste;

/// <summary>Claude (here a fake) says whether a written denial or a margin note is a lasting lesson or a one-off. The learner holds it to what the evidence is about.</summary>
public class TasteLearnerTests
{
    private static TasteEvidence Said(string words, RecipeId? recipe = null) =>
        new(Robin, TasteEvidenceKind.MarginNote, words, recipe ?? Pasta, Day(2));

    private static TasteJudgement Lasting(TasteSubjectKind kind, string subject, TasteStance stance = TasteStance.Dislikes) =>
        new TasteJudgement.Lasting([new TasteLesson(kind, subject, stance)]);

    [Fact]
    public async Task ALastingLessonBecomesALearnedNoteOnTheEvidencesDay()
    {
        var judge = new FakeTasteJudge(_ => Lasting(TasteSubjectKind.Ingredient, "Tomatoes"));

        var learned = await TasteLearner.LearnAsync([Said("never again with tomatoes, they make me gag")], judge, CancellationToken.None);

        var note = Assert.Single(learned.Notes);
        Assert.Equal(Robin, note.Person);
        Assert.Equal(TasteSubjectKind.Ingredient, note.Kind);
        Assert.Equal("tomatoes", note.Subject);
        Assert.Equal(TasteOrigin.Learned, note.Origin);
        Assert.Equal(Day(2), note.Learned);
        Assert.Equal(TasteOutcome.Lasting, Assert.Single(learned.Outcomes));
    }

    [Fact]
    public async Task AnEmptyLastingAnswerIsAOneOff()
    {
        var judge = new FakeTasteJudge(_ => new TasteJudgement.Lasting([]));

        var learned = await TasteLearner.LearnAsync([Said("hmm")], judge, CancellationToken.None);

        Assert.Empty(learned.Notes);
        Assert.Equal(TasteOutcome.OneOff, Assert.Single(learned.Outcomes));
    }

    [Theory]
    [InlineData(AuthorUnavailable.NotConfigured)]
    [InlineData(AuthorUnavailable.Offline)]
    [InlineData(AuthorUnavailable.TimedOut)]
    public async Task AnUnavailableJudgeLearnsNothingAndSaysSo(AuthorUnavailable reason)
    {
        var judge = new FakeTasteJudge(_ => new TasteJudgement.Unavailable(reason));

        var learned = await TasteLearner.LearnAsync([Said("no onions please")], judge, CancellationToken.None);

        Assert.Empty(learned.Notes);
        Assert.Equal(TasteOutcome.Unjudged, Assert.Single(learned.Outcomes));
    }

    [Fact]
    public async Task ALessonAboutADishThatIsNotTheEvidencesIsDropped()
    {
        var judge = new FakeTasteJudge(_ => Lasting(TasteSubjectKind.Dish, Tacos.Value));

        var learned = await TasteLearner.LearnAsync([Said("too crunchy", Pasta)], judge, CancellationToken.None);

        Assert.Empty(learned.Notes);
        Assert.Equal(TasteOutcome.Unjudged, Assert.Single(learned.Outcomes));
    }

    [Fact]
    public async Task ALessonAboutTheEvidencesOwnDishIsKept()
    {
        var judge = new FakeTasteJudge(_ => Lasting(TasteSubjectKind.Dish, Pasta.Value));

        var learned = await TasteLearner.LearnAsync([Said("too crunchy", Pasta)], judge, CancellationToken.None);

        Assert.Equal(Pasta.Value, Assert.Single(learned.Notes).Subject);
    }

    [Fact]
    public async Task ALessonAboutNoDishFromEvidenceWithNoDishIsDropped()
    {
        var judge = new FakeTasteJudge(_ => Lasting(TasteSubjectKind.Dish, Pasta.Value));
        var evidence = new TasteEvidence(Robin, TasteEvidenceKind.Denial, "not tonight", null, Day(0));

        var learned = await TasteLearner.LearnAsync([evidence], judge, CancellationToken.None);

        Assert.Empty(learned.Notes);
    }

    [Fact]
    public async Task AnUnknownCuisineOrABlankSubjectIsDroppedNotThrown()
    {
        var judge = new FakeTasteJudge(_ => new TasteJudgement.Lasting(
        [
            new TasteLesson(TasteSubjectKind.Cuisine, "martian", TasteStance.Dislikes),
            new TasteLesson(TasteSubjectKind.Ingredient, "  ", TasteStance.Dislikes),
        ]));

        var learned = await TasteLearner.LearnAsync([Said("yuck")], judge, CancellationToken.None);

        Assert.Empty(learned.Notes);
        Assert.Equal(TasteOutcome.Unjudged, Assert.Single(learned.Outcomes));
    }

    [Fact]
    public async Task OneBadLessonDoesNotSpoilTheGoodOnes()
    {
        var judge = new FakeTasteJudge(_ => new TasteJudgement.Lasting(
        [
            new TasteLesson(TasteSubjectKind.Cuisine, "martian", TasteStance.Dislikes),
            new TasteLesson(TasteSubjectKind.Cuisine, "mexican", TasteStance.Dislikes),
        ]));

        var learned = await TasteLearner.LearnAsync([Said("not Mexican again")], judge, CancellationToken.None);

        Assert.Equal("mexican", Assert.Single(learned.Notes).Subject);
        Assert.Equal(TasteOutcome.Lasting, Assert.Single(learned.Outcomes));
    }

    [Fact]
    public async Task AtMostThreeLessonsComeOutOfOneNote()
    {
        var judge = new FakeTasteJudge(_ => new TasteJudgement.Lasting(
            Enumerable.Range(1, 6).Select(n => new TasteLesson(TasteSubjectKind.Ingredient, "thing" + new string('x', n), TasteStance.Dislikes)).ToList()));

        var learned = await TasteLearner.LearnAsync([Said("so many things")], judge, CancellationToken.None);

        Assert.Equal(TasteLearner.MaxLessonsPerEvidence, learned.Notes.Count);
    }

    [Fact]
    public async Task TheSameLessonTwiceIsOneNote()
    {
        var judge = new FakeTasteJudge(_ => Lasting(TasteSubjectKind.Ingredient, "olives"));

        var learned = await TasteLearner.LearnAsync([Said("no olives"), Said("still no olives")], judge, CancellationToken.None);

        Assert.Single(learned.Notes);
        Assert.Equal([TasteOutcome.Lasting, TasteOutcome.Lasting], learned.Outcomes);
    }

    [Fact]
    public async Task SilenceAsksNobodyAndInventsNothing()
    {
        var judge = new FakeTasteJudge(_ => Lasting(TasteSubjectKind.Ingredient, "olives"));

        var learned = await TasteLearner.LearnAsync([], judge, CancellationToken.None);

        Assert.Empty(judge.Asked);
        Assert.Empty(learned.Notes);
        Assert.Empty(learned.Outcomes);
    }

    [Fact]
    public async Task EachPieceOfEvidenceIsAskedOnceAndInOrder()
    {
        var judge = new FakeTasteJudge(e => e.Words.Contains("always", StringComparison.Ordinal) ? Lasting(TasteSubjectKind.Ingredient, "kale") : new TasteJudgement.OneOff());

        var learned = await TasteLearner.LearnAsync([Said("not today"), Said("always hate kale"), Said("tired")], judge, CancellationToken.None);

        Assert.Equal(3, judge.Asked.Count);
        Assert.Equal([TasteOutcome.OneOff, TasteOutcome.Lasting, TasteOutcome.OneOff], learned.Outcomes);
    }

    [Fact]
    public async Task TheJudgeIsAskedOnlyForTheWordsAndWhoSaidThem()
    {
        var judge = new FakeTasteJudge(_ => new TasteJudgement.OneOff());

        await TasteLearner.LearnAsync([Said("a note")], judge, CancellationToken.None);

        var asked = Assert.Single(judge.Asked);
        Assert.Equal("a note", asked.Words);
        Assert.Equal(Robin, asked.Person);
    }

    [Fact]
    public void EvidenceNeedsWords()
    {
        Assert.Throws<ArgumentException>(() => new TasteEvidence(Robin, TasteEvidenceKind.Denial, "  ", Pasta, Day(0)).EnsureValid());
        Assert.Throws<ArgumentException>(() => new TasteEvidence(Robin, TasteEvidenceKind.Denial, new string('a', TasteEvidence.MaxWordsLength + 1), Pasta, Day(0)).EnsureValid());
    }

    [Fact]
    public async Task EvidenceWithoutWordsIsNeverSentToTheJudge()
    {
        var judge = new FakeTasteJudge(_ => new TasteJudgement.OneOff());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            TasteLearner.LearnAsync([new TasteEvidence(Robin, TasteEvidenceKind.Denial, "", Pasta, Day(0))], judge, CancellationToken.None));
        Assert.Empty(judge.Asked);
    }
}
