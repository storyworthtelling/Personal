using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.Calendar;
using Constitution.Tests.Templates;

namespace Constitution.Tests.TemplatesInTheRun;

/// <summary>Invented library, store and author for the templates step. Sam and Lee are the adults; every dish is made up.</summary>
internal static class TemplateRunData
{
    public static readonly DateOnly Week = InventedFamily.Sunday;

    /// <summary>The Wednesday before the week: the sheet's deadline (Sunday 8 am) is ahead.</summary>
    public static DateTimeOffset Before { get; } = InventedFamily.Edt(10, 21, 12);

    /// <summary>The Sunday afternoon: the deadline has passed.</summary>
    public static DateTimeOffset After { get; } = InventedFamily.Edt(10, 25, 15);

    /// <summary>Three approved templates for every slot (Sam's and Lee's weekday lines both have a pool to turn).</summary>
    public static TemplateLibrary Full() => TemplateData.Full();

    /// <summary>The full library without the approved templates of one slot.</summary>
    public static TemplateLibrary Without(MealTemplateSlot slot) =>
        new(Full().Templates.Where(template => template.Slot != slot).ToList(), []);

    public static MealTemplateSeams Seams(FakeTemplateStore store, FakeTemplateAuthor? author = null) =>
        new(store, author ?? new FakeTemplateAuthor());

    public static Task<MealTemplatesResult> RunAsync(FakeTemplateStore store, FakeTemplateAuthor? author = null, DateTimeOffset? now = null, RecordingLog? log = null) =>
        MealTemplateStep.RunAsync(Week, InventedFamily.Rules(), now ?? Before, Seams(store, author), log ?? new RecordingLog(), CancellationToken.None);
}

internal sealed class RecordingLog : IWorkoutWeekLog
{
    public List<string> Lines { get; } = [];

    public void Outcome(string phrase) => Lines.Add(phrase);
}

/// <summary>The library held as the household file would hold it; counts every read and write.</summary>
internal sealed class FakeTemplateStore : ITemplateLibraryStore
{
    public FakeTemplateStore(TemplateLibrary? library = null) => Answer = new TemplateLibraryRead.Held(library ?? TemplateLibrary.Empty, 0);

    public TemplateLibraryRead Answer { get; set; }

    public string? RefuseWith { get; set; }

    public int Loads { get; private set; }

    public int Saves { get; private set; }

    public TemplateLibrary Library => Assert.IsType<TemplateLibraryRead.Held>(Answer).Library;

    public TemplateLibraryRead Load()
    {
        Loads++;
        return Answer;
    }

    public TemplateSave Save(TemplateLibrary library)
    {
        Saves++;
        if (RefuseWith is not null)
        {
            return new TemplateSave.Refused(RefuseWith);
        }

        Answer = new TemplateLibraryRead.Held(library, 0);
        return new TemplateSave.Saved();
    }
}

internal sealed class FakeTemplateAuthor : ITemplateAuthor
{
    public List<TemplateRequest> Requests { get; } = [];

    public TemplateAuthored Answer { get; set; } = new TemplateAuthored.Drafts([]);

    public Task<TemplateAuthored> ProposeAsync(TemplateRequest request, CancellationToken cancellation)
    {
        Requests.Add(request);
        return Task.FromResult(Answer);
    }
}
