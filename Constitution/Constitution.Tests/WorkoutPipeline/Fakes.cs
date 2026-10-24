using Constitution.Domain.Calendar;
using Constitution.Domain.Notices;
using Constitution.Domain.Voice;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Delivery;
using Storyworthtelling.Remarkable;

namespace Constitution.Tests.WorkoutPipeline;

internal sealed class Access : INoticeBoardAccess
{
    public NoticeBoard Board { get; } = new();

    public T With<T>(Func<NoticeBoard, T> use) => use(Board);
}

/// <summary>The reMarkable cloud as the run meets it: documents by id and title, with the bytes kept so a test can read what is on the glass.</summary>
internal sealed class FakeTablet : IRemarkableClient
{
    private int next = 1;

    public List<string> Calls { get; } = [];

    public Dictionary<string, string> Titles { get; } = [];

    public Dictionary<string, byte[]> Pdfs { get; } = [];

    public Exception? ListFails { get; set; }

    public Exception? TrashFails { get; set; }

    public Exception? UploadFails { get; set; }

    public int Uploads => Calls.Count(call => call.StartsWith("upload:", StringComparison.Ordinal));

    public byte[] PdfOf(string title) => Pdfs[Titles.Single(pair => pair.Value == title).Key];

    public Task Upload(string title, byte[] pdfBytes) => Upload(title, pdfBytes, null);

    public Task Upload(string title, byte[] pdfBytes, string? parentFolderId)
    {
        Calls.Add("upload:" + title);
        if (UploadFails is not null)
        {
            throw UploadFails;
        }

        var id = $"doc-{next++}";
        Titles[id] = title;
        Pdfs[id] = pdfBytes;
        return Task.CompletedTask;
    }

    public Task<string> EnsureFolder(string name) => throw new InvalidOperationException("Documents are delivered at the root.");

    public Task<IReadOnlyList<RemarkableDoc>> ListRecent()
    {
        Calls.Add("list");
        if (ListFails is not null)
        {
            throw ListFails;
        }

        return Task.FromResult<IReadOnlyList<RemarkableDoc>>(Titles.Select(pair => new RemarkableDoc { Id = pair.Key, Title = pair.Value, PdfBytes = [] }).ToList());
    }

    /// <summary>The cloud's own lookup: the first document whose title matches without case, with the bytes the owner would have inked.</summary>
    public Task<RemarkableDoc?> DownloadByTitle(string title)
    {
        Calls.Add("download:" + title);
        var found = Titles.FirstOrDefault(pair => string.Equals(pair.Value, title, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<RemarkableDoc?>(found.Key is null ? null : new RemarkableDoc { Id = found.Key, Title = found.Value, PdfBytes = Pdfs[found.Key] });
    }

    public Task MoveToTrash(string docId)
    {
        Calls.Add("trash:" + docId);
        if (TrashFails is not null)
        {
            throw TrashFails;
        }

        Titles.Remove(docId);
        Pdfs.Remove(docId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeCalendarsSource : IHouseholdCalendarsSource
{
    public HouseholdCalendarSnapshot Snapshot { get; set; } = Constitution.Tests.Calendar.InventedFamily.Calendars();

    public int Calls { get; private set; }

    public DateTimeOffset? From { get; private set; }

    public DateTimeOffset? To { get; private set; }

    public Task<HouseholdCalendarSnapshot> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        Calls++;
        From = from;
        To = to;
        return Task.FromResult(Snapshot);
    }
}

internal sealed class FakeLearning : IWorkoutLearning
{
    public WorkoutLearning Held { get; set; } = WorkoutLearning.Nothing;

    public int Loads { get; private set; }

    public List<(string Person, DateOnly Week)> AssessmentWeeks { get; } = [];

    public List<string> DoctorNotes { get; } = [];

    public WorkoutLearning Load()
    {
        Loads++;
        return Held;
    }

    public void RecordAssessmentWeek(string person, DateOnly weekStart)
    {
        AssessmentWeeks.Add((person, weekStart));
        Held = Held with { AssessmentWeeks = new Dictionary<string, DateOnly>(Held.AssessmentWeeks, StringComparer.OrdinalIgnoreCase) { [person] = weekStart } };
    }

    public void RecordDoctorNotePrinted(string person)
    {
        DoctorNotes.Add(person);
        Held = Held with { Soreness = Held.Soreness.MarkDoctorNotePrinted(person) };
    }
}

internal sealed class FakeConditions : IConditionsSource
{
    public ConditionsRead Read { get; set; } = ConditionsRead.Unavailable("No forecast was loaded.");

    public int Calls { get; private set; }

    public Task<ConditionsRead> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        Calls++;
        return Task.FromResult(Read);
    }
}

internal sealed class FakeHotels : IHotelGymLookup
{
    public int Calls { get; private set; }

    public Task<HotelGymResult> FindAsync(HotelStay hotel, CancellationToken cancellation)
    {
        Calls++;
        return Task.FromResult<HotelGymResult>(new HotelGymResult.NotFound());
    }
}

internal sealed class FakeRecovery : IRecoveryReadSource
{
    public Dictionary<string, RecoveryRead> Reads { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, RecoveryRead> Read(IReadOnlyList<string> people, DateOnly morning) => Reads;
}

internal sealed class FakeEncouragement : IWorkoutEncouragement
{
    public List<ProgressionFacts> Asked { get; } = [];

    public string? Answer { get; set; } = "A little more than last time.";

    /// <summary>Whether the answer is the owner's voice; false is the plain sentence the seat falls back to.</summary>
    public bool Voiced { get; set; } = true;

    public Task<EncouragementLine?> LineAsync(ProgressionFacts news, DateTimeOffset now, CancellationToken cancellation)
    {
        Asked.Add(news);
        return Task.FromResult(Answer is null ? null : new EncouragementLine(Answer, Voiced));
    }
}

internal sealed class RecordingWeekLog : IWorkoutWeekLog
{
    public List<string> Phrases { get; } = [];

    public void Outcome(string phrase) => Phrases.Add(phrase);
}

/// <summary>An author that hands back a fixed week, so a test controls exactly which sessions the run is working with.</summary>
internal sealed class FixedAuthor(Func<WorkoutPlanRequest, HouseholdWorkoutPlan> plan) : IPlanAuthor
{
    public Task<PlanAuthorResult> AuthorAsync(WorkoutPlanRequest request, CancellationToken cancellation) =>
        Task.FromResult<PlanAuthorResult>(new PlanAuthorResult.Authored(plan(request)));
}

/// <summary>The owner's ink as the probe finds it: whatever the test says, and a record of which titles were looked at.</summary>
internal sealed class FakeInkProbe : IPageInkProbe
{
    public InkProbe Answer { get; set; } = InkProbe.Blank;

    public List<string> Asked { get; } = [];

    public Task<InkProbe> ProbeAsync(string title, CancellationToken cancellation)
    {
        Asked.Add(title);
        return Task.FromResult(Answer);
    }
}
