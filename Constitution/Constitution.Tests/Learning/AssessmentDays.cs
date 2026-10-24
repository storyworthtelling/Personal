using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;
using PlannedTest = Constitution.Domain.Workouts.AssessmentTest;

namespace Constitution.Tests.Learning;

/// <summary>An assessment day as the read-back meets it: the real pages, ink on the named boxes, resolved by the real resolver.</summary>
internal static class AssessmentDays
{
    private static readonly DateTimeOffset Slot = Est(11, 3, 18);

    public static (ExpectedDocument Document, DocumentResult Result) Strength(LearningFolder _, string person, string? first, string? second) =>
        Day(PlannedTest.StrengthBaselines, person, [first, second]);

    public static (ExpectedDocument Document, DocumentResult Result) Walk(string person, string distance) =>
        Day(PlannedTest.TimedWalk, person, [distance]);

    private static (ExpectedDocument Document, DocumentResult Result) Day(PlannedTest test, string person, string?[] answers)
    {
        var session = AssessmentPlanner.Plan(Request(Calendars(), bothAdults: false)).Sessions.First(item => item.Test == test);
        var model = AssessmentDocumentBuilder.Build(session, Slot, Zone, 1, 3, []);
        var day = new DateOnly(2026, 11, 3);
        var document = AssessmentDocumentBuilder.Expected(model, session, person, day, null);
        var pages = document.Pages
            .Select(page =>
            {
                var answer = answers[page.Number - AssessmentDocument.PageOfTest(0)];
                var read = InkPageRead.Read(page.AskedTargets.Select(target =>
                    target.Id == WorkoutIds.Result && answer is not null
                        ? new InkTargetRead(target.Id, true, 0.95, answer)
                        : new InkTargetRead(target.Id, false, 0.95)));
                return new PageResolutions(page.Number, new InkResolver(page.Readout).Resolve(read, page.Rules, Slot.AddDays(1)));
            })
            .ToList();
        return (document, new DocumentResult(document.Name, document.Kind, day, person, Slot.AddDays(1), pages));
    }
}
