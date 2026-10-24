using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.ReadBack;
using Constitution.Tests.Documents;
using Constitution.Tests.ReadBack;

namespace Constitution.Tests.Learning;

/// <summary>A temp folder for the learned files, and the documents and results the learning is settled from. Invented people only.</summary>
internal sealed class LearningFolder : IDisposable
{
    public LearningFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "learning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public FileLedgerStore Ledger() => new(File("skips.json"));

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal static class Resolved
{
    public static readonly DateOnly Day = ReadBackFixture.Day;

    /// <summary>The squat and the row, with the squat page's marks named; every other box confidently empty.</summary>
    public static (ExpectedDocument Document, DocumentResult Result) Strength(params (int Page, string Target)[] marks) =>
        Of(ReadBackFixture.Workout(), marks);

    public static (ExpectedDocument Document, DocumentResult Result) WithSoreness(string place, params (int Page, string Target)[] marks)
    {
        var model = WorkoutSamples.Workout(new Soreness(place, "Monday", 2, false), [WorkoutSamples.Squat(), WorkoutSamples.Row()]);
        var document = ExpectedDocument.ForWorkout(model, ReadBackFixture.Person, SessionKind.Strength, Day);
        return Of(document, marks);
    }

    public static (ExpectedDocument Document, DocumentResult Result) Of(ExpectedDocument document, params (int Page, string Target)[] marks)
    {
        var read = ReadBackFixture.Read(document, PenStrokes.Seen, marks);
        var pages = document.Pages
            .Select(page => new PageResolutions(
                page.Number, new InkResolver(page.Readout).Resolve(read.PageFor(page.Number), page.Rules, ReadBackFixture.Morning)))
            .ToList();
        return (document, new DocumentResult(document.Name, document.Kind, document.Day, document.Person, ReadBackFixture.Morning, pages));
    }
}
