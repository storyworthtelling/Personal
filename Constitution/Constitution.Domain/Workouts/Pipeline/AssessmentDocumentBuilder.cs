using System.Globalization;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using PlannedTest = Constitution.Domain.Workouts.AssessmentTest;

namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>
/// Turns one assessment session (C-5.1.2) into the pages of its day (C-2.3.4) and into what the morning read-back
/// reads off them. One test page per measure: the strength day asks for two counted numbers, the walk for one distance,
/// the mobility day for one circled word. Nothing on them is scored (D27), and no figure or voiced line is asked for.
///
/// <para>The instructions are the agent's plain wording of the four tests the catalogue names; no coach has read them.</para>
///
/// <para>The page paints its number box as <c>result</c> and its circle as <c>choice</c>, on every test page alike, so the
/// baselines are read by page and measure (<see cref="AssessmentBaselines.FromPages"/>), not by the catalogue's own ids.</para>
/// </summary>
public static class AssessmentDocumentBuilder
{
    public static AssessmentDocumentModel Build(
        AssessmentSession session,
        DateTimeOffset start,
        TimeZoneInfo zone,
        int number,
        int ofDays,
        IReadOnlyList<DateOnly> otherDays)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(otherDays);

        var tests = session.Measures.Select(Test).ToList();
        return new AssessmentDocumentModel(TimeZoneInfo.ConvertTime(start, zone).DateTime, number, ofDays, tests, Schedule(otherDays));
    }

    /// <summary>The document as the read-back will meet it: page <c>n + 1</c> is test <c>n</c>, read by a number rule or a circle rule.</summary>
    public static ExpectedDocument Expected(AssessmentDocumentModel model, AssessmentSession session, string person, DateOnly day, string? subject)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(session);

        var layouts = AssessmentDocument.Build(model);
        var pages = new List<ExpectedPage>();
        for (var index = 0; index < model.Tests.Count; index++)
        {
            var test = model.Tests[index];
            InkRule rule = test.Words is null
                ? new NumberRule(test.Name, WorkoutIds.Result)
                : new RatingRule(test.Name, WorkoutIds.Choice);
            pages.Add(new ExpectedPage(AssessmentDocument.PageOfTest(index), new InkReadout(layouts[index + 1].Map), [rule]));
        }

        return new ExpectedDocument(Delivery.DeliveryKind.Workout, day, subject, person, session.Placed.Session.Kind, pages);
    }

    private static Documents.AssessmentTest Test(AssessmentMeasure measure) => measure switch
    {
        NumberMeasure number => new Documents.AssessmentTest(number.Name, Instruction(number.Id), Hint(number.Id), number.Unit, null, false, null),
        CircleMeasure circle => new Documents.AssessmentTest(circle.Name, Instruction(circle.Id), Hint(circle.Id), null, circle.Words, false, null),
        _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, "This kind of measure has no page."),
    };

    private static string Instruction(string id) => id switch
    {
        AssessmentCatalogue.SitToStandId =>
            "Sit on a sturdy chair, then stand all the way up and sit down again, as many times as you can in 30 seconds. Count each full stand.",
        AssessmentCatalogue.WallPushUpsId =>
            "Stand an arm's length from a wall with your hands on it at chest height. Lean in and push back, as many times as you can in 30 seconds.",
        AssessmentCatalogue.TimedWalkId =>
            "Walk, or jog if you like, for 10 minutes at a pace you could keep up. When the time is up, note how far you went.",
        AssessmentCatalogue.ToeReachId =>
            "Sit on a chair with one leg straight out. Reach toward your toes and circle the lowest place your hands get to.",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "There are no instructions for this measure."),
    };

    private static string? Hint(string id) => id switch
    {
        AssessmentCatalogue.SitToStandId => "Stop at 30 seconds, even mid-stand. Use your arms only if you must.",
        AssessmentCatalogue.WallPushUpsId => "Keep your body in one line. A smaller lean counts as much as a deeper one.",
        AssessmentCatalogue.TimedWalkId => "A phone map or a treadmill display will tell you the distance.",
        AssessmentCatalogue.ToeReachId => "Go only as far as is comfortable.",
        _ => null,
    };

    private static string Schedule(IReadOnlyList<DateOnly> otherDays)
    {
        var names = otherDays.Order().Select(day => day.ToString("dddd", CultureInfo.InvariantCulture)).ToList();
        return names.Count switch
        {
            0 => "This is the only session this week.",
            1 => $"The other session is on {names[0]}.",
            _ => $"The other sessions are on {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}.",
        };
    }
}
