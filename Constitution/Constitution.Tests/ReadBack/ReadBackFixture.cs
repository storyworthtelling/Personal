using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Tests.Documents;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// The invented documents every read-back test stands on. A workout of two exercises is page 1 (the cover, no
/// boxes), page 2 (the squat) and page 3 (the row); each exercise page carries a Done tick and an
/// easy / right / hard choice, under the same ids on both pages, exactly as the painter draws them.
/// </summary>
internal static class ReadBackFixture
{
    /// <summary>Invented; nothing in a prompt or a trail line may ever hold it.</summary>
    public const string Person = "Juniper";

    public const double Sure = 0.95;

    public static readonly DateOnly Day = new(2026, 11, 3);

    /// <summary>The morning after, at the hour the worker runs, on a clock five hours behind UTC.</summary>
    public static readonly DateTimeOffset Morning = new(2026, 11, 4, 7, 15, 0, TimeSpan.FromHours(-5));

    public static WorkoutDocumentModel TwoExercises() =>
        WorkoutSamples.Workout(exercises: [WorkoutSamples.Squat(), WorkoutSamples.Row()]);

    public static ExpectedDocument Workout(DateOnly? day = null, string? subject = null) =>
        ExpectedDocument.ForWorkout(TwoExercises(), Person, SessionKind.Strength, day ?? Day, subject);

    public static ExpectedDocument Digest(DateOnly? day = null) =>
        new(DeliveryKind.Digest, day ?? Day, null, Person, null, []);

    /// <summary>A meals document of one page: the dinner row of the shared ink fixture, due at its deadline.</summary>
    public static ExpectedDocument Meals(DateOnly? day = null)
    {
        var fixture = new Constitution.Tests.Ink.InkFixture();
        return new ExpectedDocument(
            DeliveryKind.Meals, day ?? Day, null, Person, null,
            [new ExpectedPage(1, fixture.Readout, [Constitution.Tests.Ink.InkFixture.Meal])]);
    }

    /// <summary>Every box the page's rules read, confidently empty.</summary>
    public static IEnumerable<InkTargetRead> EmptyBoxes(ExpectedPage page, double confidence = Sure) =>
        page.AskedTargets.Select(target => new InkTargetRead(target.Id, false, confidence));

    /// <summary>
    /// The whole document read, every box confidently empty but for the named marks. A mark is a page number and a
    /// target id ("done" on page 2); a page the document has but the marks do not name is read and empty.
    /// </summary>
    public static InkDocumentRead Read(ExpectedDocument document, PenStrokes strokes, params (int Page, string Target)[] marks)
    {
        var pages = new Dictionary<int, InkPageRead>();
        foreach (var page in document.Pages)
        {
            var reads = page.AskedTargets.Select(target =>
                marks.Contains((page.Number, target.Id))
                    ? new InkTargetRead(target.Id, true, Sure)
                    : new InkTargetRead(target.Id, false, Sure));
            pages[page.Number] = InkPageRead.Read(reads);
        }

        return new InkDocumentRead(pages, strokes);
    }

    /// <summary>The same read with one box's confidence lowered to <paramref name="confidence"/>.</summary>
    public static InkDocumentRead WithDoubt(ExpectedDocument document, int pageNumber, string targetId, double confidence, bool marked)
    {
        var pages = new Dictionary<int, InkPageRead>();
        foreach (var page in document.Pages)
        {
            var reads = page.AskedTargets.Select(target =>
                page.Number == pageNumber && target.Id == targetId
                    ? new InkTargetRead(target.Id, marked, confidence)
                    : new InkTargetRead(target.Id, false, Sure));
            pages[page.Number] = InkPageRead.Read(reads);
        }

        return new InkDocumentRead(pages, PenStrokes.Unknown);
    }

    /// <summary>Every page not read, with a reason.</summary>
    public static InkDocumentRead NothingRead(ExpectedDocument document) =>
        new(document.Pages.ToDictionary(page => page.Number, _ => InkPageRead.NotRead("The reply could not be read.")), PenStrokes.Unknown);
}
