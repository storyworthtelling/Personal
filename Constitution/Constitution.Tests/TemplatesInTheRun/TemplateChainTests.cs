using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.MealWeekDocuments;
using Constitution.Tests.MealWeekRun;
using Constitution.Tests.Templates;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.TemplatesInTheRun.TemplateRunData;
using Run = Constitution.Infrastructure.Meals.MealWeekRun;

namespace Constitution.Tests.TemplatesInTheRun;

/// <summary>
/// The chain end to end with fakes at the edges only: a run proposes and prints, the recorded expected document carries the lines, synthetic ink goes through the
/// real resolver and the real deadline rule, and the sheet's feedback settles the right templates. The read-back's own call into the feedback is a later job.
/// </summary>
public class TemplateChainTests
{
    private static readonly PersonId SamId = new("sam");

    private static readonly PersonId LeeId = new("lee");

    [Fact]
    public async Task InkOnTheRecordedPagesSettlesTheRightTemplates()
    {
        var rig = new MealWeekRig();
        var docs = new DocsRig();
        rig.Box.Of(DocsData.Cards().Concat(MealPlanning.MealData.Box()).ToList());
        var store = new FakeTemplateStore(TemplateRunData.Without(MealTemplateSlot.WeekendBreakfast));
        var author = new FakeTemplateAuthor { Answer = new TemplateAuthored.Drafts([new TemplateDraft(MealTemplateSlot.WeekendBreakfast, "Fruit salad and toast")]) };
        var seams = rig.Seams with { Documents = docs.Seams, Templates = new MealTemplateSeams(store, author) };
        await Run.RunAsync(rig.Inputs() with { DeliverFor = [Lee] }, seams, CancellationToken.None);
        var expected = Assert.Single(docs.Expected.Recorded);
        var library = store.Library;
        var proposal = Assert.Single(library.Proposed);
        var boxFruit = library.Prints.Single(print => print.ItemId == TemplateItems.YesId(MealTemplateSlot.BoxFruit, 0)).TemplateId;

        // Both adults mark their own copy of the sheet before the deadline: each ticks the new weekend breakfast; Lee also writes a reason on the lunchbox fruit; the rest is left blank.
        var weekendYes = TemplateItems.YesId(MealTemplateSlot.WeekendBreakfast, 0);
        var fruitYes = TemplateItems.YesId(MealTemplateSlot.BoxFruit, 0);
        var deadline = MealDeadline.ForWeek(Week, Zone);
        var readAt = deadline.AddMinutes(-60);
        var answers = new List<MealAnswer>();
        foreach (var person in new[] { SamId, LeeId })
        {
            foreach (var page in expected.Pages.Skip(1))
            {
                var reads = page.Readout.Map.Targets.Select(target => target.Id switch
                {
                    var id when id == weekendYes => new InkTargetRead(id, true, 0.95),
                    var id when person == LeeId && id == MealSheetIds.BoxFruitReason => new InkTargetRead(id, true, 0.95, "Robin will not eat it"),
                    var id => new InkTargetRead(id, false, 0.95),
                }).ToList();
                var resolved = new InkResolver(page.Readout).Resolve(InkPageRead.Read(reads), page.Rules, readAt);
                answers.AddRange(resolved.Select(resolution => MealAnswerInk.From(resolution, person, Week, readAt)).OfType<MealAnswer>());
            }
        }

        var items = expected.Pages.SelectMany(page => page.Rules).OfType<MealItemRule>().Select(rule => rule.YesBoxId).ToList();
        var decision = MealDeadline.Decide(answers, deadline, deadline.AddMinutes(5), items);
        var feedback = TemplateFeedback.From(decision, answers, library, Week, Week);

        Assert.Equal(TemplateStatus.Approved, feedback.Library.Find(proposal.Id)!.Status);
        Assert.Equal(TemplateApproval.Explicit, feedback.Library.Find(proposal.Id)!.Approval);
        Assert.Equal(TemplateStatus.Rejected, feedback.Library.Find(boxFruit)!.Status);
        Assert.Contains("Robin will not eat it", feedback.Library.Find(boxFruit)!.Reason, StringComparison.Ordinal);
        Assert.Empty(feedback.Library.Proposed);
        Assert.Contains(feedback.Evidence, item => item.Recipe?.Value == boxFruit);
        Assert.Equal(2, feedback.Likes.Count);
    }

    [Fact]
    public async Task TheExpectedLinesAreTheTemplateLinesAndNothingElse()
    {
        var rig = new MealWeekRig();
        var docs = new DocsRig();
        rig.Box.Of(DocsData.Cards().Concat(MealPlanning.MealData.Box()).ToList());
        var store = new FakeTemplateStore(TemplateData.Full());
        await Run.RunAsync(rig.Inputs() with { DeliverFor = [Lee] }, rig.Seams with { Documents = docs.Seams, Templates = new MealTemplateSeams(store, new FakeTemplateAuthor()) }, CancellationToken.None);

        var expected = Assert.Single(docs.Expected.Recorded);
        var model = expected.Pages.Skip(1).Take(2).SelectMany(page => page.Rules).OfType<MealItemRule>().Select(rule => rule.YesBoxId).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(11, model.Count);
        Assert.Equal(store.Library.Prints.Select(print => print.ItemId).Order(StringComparer.Ordinal), model);
    }
}
