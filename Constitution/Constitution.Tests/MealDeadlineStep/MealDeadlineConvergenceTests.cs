using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Tests.MealAnswers;
using Constitution.Tests.MealTaste;
using Constitution.Tests.TemplatesInTheRun;
using static Constitution.Tests.MealAnswers.MealAnswerData;
using static Constitution.Tests.MealDeadlineStep.DeadlineRig;

namespace Constitution.Tests.MealDeadlineStep;

/// <summary>
/// The two-install simulation's answers (seeded readings from twenty hours before the deadline to ten hours after it, two skewed clocks, every state either file
/// was ever in handed over in a seeded order with repeats) fed to the deadline step on the planner's phone: whatever order the files arrived in, it decides the
/// same, teaches the same, and leaves the same library and the same record. A failure prints the seed.
/// </summary>
public class MealDeadlineConvergenceTests
{
    private static readonly string[] TemplateLines = ["weekend-breakfast-yes", "weekend-lunch-yes", "box-sandwich-yes"];

    private static TemplateLibrary Proposed() => new(
        [
            new MealTemplate("tpl-weekend-breakfast-pancakes", MealTemplateSlot.WeekendBreakfast, "pancakes"),
            new MealTemplate("tpl-weekend-lunch-soup", MealTemplateSlot.WeekendLunch, "soup"),
            new MealTemplate("tpl-box-sandwich-egg", MealTemplateSlot.BoxSandwich, "egg sandwich"),
        ],
        [
            new TemplatePrint(Week, "weekend-breakfast-yes", "tpl-weekend-breakfast-pancakes"),
            new TemplatePrint(Week, "weekend-lunch-yes", "tpl-weekend-lunch-soup"),
            new TemplatePrint(Week, "box-sandwich-yes", "tpl-box-sandwich-egg"),
        ]);

    private static async Task<string> SignatureAsync(DeadlineRig rig, IEnumerable<MealAnswer> answers)
    {
        rig.Redecide();
        rig.Notes.Kept.Clear();
        rig.Library.Answer = new TemplateLibraryRead.Held(Proposed(), 0);
        rig.Answers.All.Clear();
        rig.Answers.All.AddRange(answers);
        rig.Judge = new FakeTasteJudge(_ => new TasteJudgement.OneOff());
        var report = await rig.DecidedAsync();

        var entry = rig.Decisions.Held().For(Week)!;
        var notes = rig.Notes.Kept.OrderBy(note => note.Person.Value, StringComparer.Ordinal).ThenBy(note => note.Subject, StringComparer.Ordinal)
            .Select(note => $"{note.Person.Value}:{note.Subject}:{note.Stance}");
        var templates = rig.Library.Library.Templates.OrderBy(template => template.Id, StringComparer.Ordinal)
            .Select(template => $"{template.Id}:{template.Status}:{template.Approval}");
        var asked = rig.Judge.Asked.Select(item => $"{item.Person.Value}:{item.Recipe?.Value}:{item.Words}").Order(StringComparer.Ordinal);
        return string.Join('|', report.Approved, report.Denied, report.ApprovedUnread, report.LateSetAside, report.TemplatesFed, report.LikesTaught,
            string.Join(',', report.DeniedEvenings), string.Join(',', entry.ExtraLines),
            string.Join(',', notes), string.Join(',', templates), string.Join(',', asked), entry.Approved, entry.Denied);
    }

    [Fact]
    public async Task TheVerdictAndWhatItTeachesDoNotDependOnTheOrderTheFilesArrived()
    {
        var rig = await PlannedAsync();
        for (var seed = 1; seed <= MealAnswerSimulation.SeedCount; seed++)
        {
            var simulation = MealAnswerSimulation.Run(seed);
            var one = MealAnswerRecords.Read(simulation.Arrive(1), Week).Answers;
            var two = MealAnswerRecords.Read(simulation.Arrive(2), Week).Answers.Reverse();
            var truth = MealAnswerRecords.Read(simulation.Truth, Week).Answers;

            var first = await SignatureAsync(rig, one);
            var second = await SignatureAsync(rig, two);
            var third = await SignatureAsync(rig, truth);

            Assert.True(first == second && second == third, $"Seed {seed}: the step decided differently by arrival order.\n{first}\n{second}\n{third}");
        }
    }

    [Fact]
    public async Task EveryLineIsDecidedAndAnApprovedLineIsNeverUndone()
    {
        var rig = await PlannedAsync();
        for (var seed = 1; seed <= MealAnswerSimulation.SeedCount; seed++)
        {
            var truth = MealAnswerRecords.Read(MealAnswerSimulation.Run(seed).Truth, Week).Answers;
            rig.Redecide();
            rig.Notes.Kept.Clear();
            rig.Library.Answer = new TemplateLibraryRead.Held(Proposed(), 0);
            rig.Answers.All.Clear();
            rig.Answers.All.AddRange(truth);

            var report = await rig.DecidedAsync();

            Assert.True(report.Approved + report.Denied == 7 + TemplateLines.Length, $"Seed {seed}: {report.Approved} + {report.Denied} lines.");
            Assert.True(report.ApprovedUnread <= report.Approved, $"Seed {seed}.");
        }
    }
}
