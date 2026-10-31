using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;

namespace Constitution.Tests.ClaudeTasteJudge;

/// <summary>The reply is read strictly: exactly the asked-for shape, or no judgement at all.</summary>
public class TasteJudgeReplyTests
{
    private static readonly RecipeId Card = new("buttered-noodles");

    private static TasteJudgement Parse(string? text, RecipeId? recipe = null) => TasteJudgeReply.Parse(text, recipe);

    [Fact]
    public void ALastingReplyGivesItsLessons()
    {
        var reply = "{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"ingredient\",\"subject\":\"Mushrooms\",\"stance\":\"dislikes\"}]}";

        var lasting = Assert.IsType<TasteJudgement.Lasting>(Parse(reply, Card));

        Assert.Equal([new TasteLesson(TasteSubjectKind.Ingredient, "Mushrooms", TasteStance.Dislikes)], lasting.Lessons);
    }

    [Fact]
    public void ADishLessonIsAboutTheEvidencesOwnCard()
    {
        var reply = "{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"dish\",\"stance\":\"dislikes\"}]}";

        var lasting = Assert.IsType<TasteJudgement.Lasting>(Parse(reply, Card));

        Assert.Equal([new TasteLesson(TasteSubjectKind.Dish, "buttered-noodles", TasteStance.Dislikes)], lasting.Lessons);
    }

    [Fact]
    public void ADishLessonWithNoCardIsUnusable()
    {
        var reply = "{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"dish\",\"stance\":\"likes\"}]}";

        Assert.Equal(AuthorUnavailable.ReplyUnusable, Assert.IsType<TasteJudgement.Unavailable>(Parse(reply, null)).Reason);
    }

    [Fact]
    public void AOneOffReplyIsAOneOff()
    {
        Assert.IsType<TasteJudgement.OneOff>(Parse("{\"verdict\":\"oneoff\"}", Card));
        Assert.IsType<TasteJudgement.OneOff>(Parse("{\"verdict\":\"oneoff\",\"lessons\":[]}", Card));
    }

    [Fact]
    public void AFencedReplyIsStillRead()
    {
        Assert.IsType<TasteJudgement.OneOff>(Parse("```json\n{\"verdict\":\"oneoff\"}\n```", Card));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I am not able to help with that.")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"verdict\":\"maybe\"}")]
    [InlineData("{\"verdict\":\"lasting\"}")]
    [InlineData("{\"verdict\":\"lasting\",\"lessons\":[]}")]
    [InlineData("{\"verdict\":\"oneoff\",\"lessons\":[{\"kind\":\"cuisine\",\"subject\":\"french\",\"stance\":\"likes\"}]}")]
    [InlineData("{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"colour\",\"subject\":\"red\",\"stance\":\"likes\"}]}")]
    [InlineData("{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"cuisine\",\"subject\":\"french\",\"stance\":\"adores\"}]}")]
    [InlineData("{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"cuisine\",\"stance\":\"likes\"}]}")]
    [InlineData("{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"cuisine\",\"subject\":7,\"stance\":\"likes\"}]}")]
    [InlineData("{\"verdict\":\"lasting\",\"lessons\":\"all of them\"}")]
    [InlineData("{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"cuisine\",\"subject\":\"french\",\"stance\":\"likes\"},{\"kind\":\"cuisine\",\"subject\":\"indian\",\"stance\":\"likes\"},{\"kind\":\"cuisine\",\"subject\":\"asian\",\"stance\":\"likes\"},{\"kind\":\"cuisine\",\"subject\":\"mexican\",\"stance\":\"likes\"}]}")]
    public void AnythingElseIsNoJudgement(string? text)
    {
        var unavailable = Assert.IsType<TasteJudgement.Unavailable>(Parse(text, Card));

        Assert.Equal(AuthorUnavailable.ReplyUnusable, unavailable.Reason);
        Assert.False(string.IsNullOrWhiteSpace(unavailable.Detail));
    }

    [Fact]
    public void ASubjectOverTheNoteLimitIsUnusable()
    {
        var long60 = new string('a', TasteNote.MaxSubjectLength + 1);
        var reply = "{\"verdict\":\"lasting\",\"lessons\":[{\"kind\":\"ingredient\",\"subject\":\"" + long60 + "\",\"stance\":\"dislikes\"}]}";

        Assert.Equal(AuthorUnavailable.ReplyUnusable, Assert.IsType<TasteJudgement.Unavailable>(Parse(reply, Card)).Reason);
    }

    [Fact]
    public void AReplyOverTheLimitIsUnusableEvenIfItIsValidJson()
    {
        var padded = "{\"verdict\":\"oneoff\",\"note\":\"" + new string('x', TasteJudgeReply.LongestReply) + "\"}";

        Assert.Equal(AuthorUnavailable.ReplyUnusable, Assert.IsType<TasteJudgement.Unavailable>(Parse(padded, Card)).Reason);
    }

    [Fact]
    public void TheDetailQuotesNothingFromTheReply()
    {
        var unavailable = Assert.IsType<TasteJudgement.Unavailable>(Parse("my secret sample sentence", Card));

        Assert.DoesNotContain("secret", unavailable.Detail, StringComparison.Ordinal);
    }
}
