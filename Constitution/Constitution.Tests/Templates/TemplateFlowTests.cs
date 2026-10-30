using Constitution.Domain.Documents;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using static Constitution.Tests.MealAnswers.MealAnswerData;
using static Constitution.Tests.Templates.TemplateData;

namespace Constitution.Tests.Templates;

/// <summary>A proposal's whole life, pure end to end: proposed, printed once, answered on the sheet, then in or out of the rotation.</summary>
public class TemplateFlowTests
{
    [Fact]
    public void AWordTooWideForALineIsNotATemplate()
    {
        Assert.False(MealTemplate.Prints(new string('w', 23)));
        Assert.True(MealTemplate.Prints("Oats with sliced banana"));
        Assert.Throws<ArgumentException>(() => new MealTemplate("tpl-weekend-lunch-x", MealTemplateSlot.WeekendLunch, new string('w', 23)));
    }

    [Fact]
    public void TheLongestPrintableWordsRenderOnTheSheet()
    {
        var longest = "wholegrain porridge with berries"; // 32 characters, two lines of 22
        var library = new TemplateLibrary(Full().Templates.Where(template => template.Slot != MealTemplateSlot.WeekendLunch).Append(Approved("tpl-weekend-lunch-long", MealTemplateSlot.WeekendLunch, longest)).ToList(), []);

        var model = MealSheetBuilder.Build(BuilderTests.Request(TemplateRotation.Pick(library, Week, Facts).Daily));

        Assert.Equal(4, MealSheetDocument.Build(model).Count);
    }

    [Fact]
    public void AProposalIsPrintedAnsweredAndThenRotatedOrDropped()
    {
        var pending = TemplateProposals.Merge(Full(), [new TemplateDraft(MealTemplateSlot.WeekendBreakfast, "Fruit salad and toast"), new TemplateDraft(MealTemplateSlot.BoxFruit, "Cold melon")]).Library;
        var firstSheet = TemplateRotation.Pick(pending, Week, Facts);
        var printed = pending.WithPrints(firstSheet.Prints);
        Assert.Equal("Fruit salad and toast", firstSheet.Daily!.WeekendBreakfast);
        Assert.Equal("Cold melon", firstSheet.Daily.Lunchbox.Fruit);

        var model = MealSheetBuilder.Build(BuilderTests.Request(firstSheet.Daily));
        var expected = MealAnswerRecords.ExpectedItems(model);
        var answers = new[]
        {
            Tick(Sam, "weekend-breakfast-yes", Before(60)),
            Deny(Lee, "box-fruit-yes", "Robin will not touch melon", Before(30)),
        };
        var decision = MealDeadline.Decide(answers, Deadline, After(5), expected);

        var feedback = TemplateFeedback.From(decision, answers, printed, Week, Week);

        Assert.Equal(TemplateStatus.Approved, feedback.Library.Find(FindId(feedback.Library, "Fruit salad and toast"))!.Status);
        Assert.Equal(TemplateStatus.Rejected, feedback.Library.Find(FindId(feedback.Library, "Cold melon"))!.Status);
        Assert.Single(feedback.Likes);
        Assert.Single(feedback.Evidence);
        Assert.Empty(feedback.Library.Proposed);

        for (var weeks = 1; weeks < 12; weeks++)
        {
            var later = TemplateRotation.Pick(feedback.Library, WeekAfter(weeks), Facts);
            Assert.NotEqual("Cold melon", later.Daily!.Lunchbox.Fruit);
        }
    }

    private static string FindId(TemplateLibrary library, string words) => library.Templates.Single(template => template.Words == words).Id;
}
