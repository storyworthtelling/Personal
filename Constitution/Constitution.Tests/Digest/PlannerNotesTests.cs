using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Documents;
using Constitution.Infrastructure.ReadBack;
using Constitution.Tests.MealRanking;
using Constitution.Tests.ReadBack;

namespace Constitution.Tests.DigestPages;

/// <summary>
/// Owner decision (amends C-2.2.3): the four planner-note lines on the digest are read at Sunday's run and carried into the planner's inputs. Nothing else on the
/// digest is read (Body stays home, D165), the read happens once, the page is still trashed on day three, and a week with no notes plans byte for byte as before.
/// </summary>
public class PlannerNotesTests : IDisposable
{
    private static readonly DateOnly Saturday = new(2026, 11, 7);
    private static readonly DateTimeOffset SundayMorning = new(2026, 11, 8, 7, 15, 0, TimeSpan.FromHours(-5));
    private readonly string folder = Path.Combine(Path.GetTempPath(), "planner-notes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    private static ExpectedDocument Digest() =>
        ExpectedDocument.ForDigest(DigestMeals.NextWeekPage(new DigestNextWeek([], []), Saturday, 5, 5), Saturday, "Juniper");

    private static InkDocumentRead Written(ExpectedDocument document, params (string Id, string? Text)[] lines)
    {
        var page = document.Pages[0];
        var reads = page.AskedTargets.Select(target =>
        {
            var line = lines.FirstOrDefault(item => item.Id == target.Id);
            return line.Id is null ? new InkTargetRead(target.Id, false, 0.95) : new InkTargetRead(target.Id, true, 0.95, line.Text);
        });
        return new InkDocumentRead(new Dictionary<int, InkPageRead> { [page.Number] = InkPageRead.Read(reads) }, PenStrokes.Seen);
    }

    private (Rig Rig, FilePlannerNotes Notes, ReadBackService Service) Make(Func<ExpectedDocument, InkDocumentRead> reply)
    {
        var rig = new Rig(true, Digest());
        rig.Vision.Reply = reply;
        var notes = new FilePlannerNotes(Path.Combine(folder, "planner-notes.json"));
        var service = new ReadBackService(rig.Expected, rig.Tablet, rig.Vision, rig.Results, rig.Ledger, rig.Proof, rig.Notices, rig.Log, () => rig.Now, plannerNotes: notes);
        return (rig, notes, service);
    }

    [Fact]
    public async Task NotReadOnSaturdayItself()
    {
        var (rig, notes, service) = Make(document => Written(document, (DigestIds.PlannerNotes[0], "no fish")));
        rig.Now = SundayMorning.AddDays(-1);
        await service.Run();
        Assert.Equal(0, rig.Vision.Reads);
        Assert.False(notes.Has("Juniper", Saturday));
    }

    [Fact]
    public async Task ReadOnSundayAndSavedInOrder()
    {
        var (rig, notes, service) = Make(document => Written(document, (DigestIds.PlannerNotes[2], "Soup on Thursday"), (DigestIds.PlannerNotes[0], "No fish this week")));
        rig.Now = SundayMorning;
        var report = await service.Run();
        Assert.IsType<DocumentReadBack.DigestKept>(Assert.Single(report.Documents));
        Assert.Equal(1, rig.Vision.Reads);
        Assert.True(notes.Has("Juniper", Saturday));
        Assert.Equal(["No fish this week", "Soup on Thursday"], notes.For(Saturday.AddDays(8)));
        Assert.Empty(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task ReadOnlyOnce()
    {
        var (rig, _, service) = Make(document => Written(document, (DigestIds.PlannerNotes[0], "No fish")));
        rig.Now = SundayMorning;
        await service.Run();
        rig.Now = SundayMorning.AddDays(1);
        await service.Run();
        Assert.Equal(1, rig.Vision.Reads);
    }

    [Fact]
    public async Task NothingWrittenIsStillOneRead()
    {
        var (rig, notes, service) = Make(document => Written(document));
        rig.Now = SundayMorning;
        await service.Run();
        Assert.True(notes.Has("Juniper", Saturday));
        Assert.Empty(notes.For(Saturday.AddDays(8)));
        rig.Now = SundayMorning.AddDays(1);
        await service.Run();
        Assert.Equal(1, rig.Vision.Reads);
    }

    [Fact]
    public async Task InkThatCannotBeMadeOutIsNoNote()
    {
        var (rig, notes, service) = Make(document => Written(document, (DigestIds.PlannerNotes[0], null)));
        rig.Now = SundayMorning;
        await service.Run();
        Assert.Empty(notes.For(Saturday.AddDays(8)));
    }

    [Fact]
    public async Task AFailedReadIsTriedAgainAndNotMarkedRead()
    {
        var (rig, notes, service) = Make(document => Written(document));
        rig.Vision.Fails = new HttpRequestException("busy");
        rig.Now = SundayMorning;
        var report = await service.Run();
        Assert.IsType<DocumentReadBack.Failed>(Assert.Single(report.Documents));
        Assert.False(notes.Has("Juniper", Saturday));
        rig.Vision.Fails = null;
        await service.Run();
        Assert.True(notes.Has("Juniper", Saturday));
    }

    [Fact]
    public async Task StillTrashedOnDayThree()
    {
        var (rig, _, service) = Make(document => Written(document, (DigestIds.PlannerNotes[0], "No fish")));
        rig.Now = SundayMorning;
        await service.Run();
        rig.Now = new DateTimeOffset(2026, 11, 10, 7, 15, 0, TimeSpan.FromHours(-5));
        var report = await service.Run();
        Assert.IsType<DocumentReadBack.DigestTrashed>(Assert.Single(report.Documents));
        Assert.Single(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task OnlyTheFourNoteIdsAreEverAskedFor()
    {
        var (rig, _, service) = Make(document => Written(document));
        rig.Now = SundayMorning;
        await service.Run();
        var asked = rig.Vision.LastDocument!.Pages.SelectMany(page => page.AskedTargets).Select(target => target.Id).Order().ToList();
        Assert.Equal(DigestIds.PlannerNotes.Order(), asked);
        Assert.Equal(0, rig.Ledger.Saves);
    }

    [Fact]
    public async Task ADigestWithoutThePageIsStillNeverRead()
    {
        var rig = new Rig(true, ReadBackFixture.Digest(Saturday));
        var notes = new FilePlannerNotes(Path.Combine(folder, "planner-notes.json"));
        var service = new ReadBackService(rig.Expected, rig.Tablet, rig.Vision, rig.Results, rig.Ledger, rig.Proof, rig.Notices, rig.Log, () => SundayMorning, plannerNotes: notes);
        await service.Run();
        Assert.Equal(0, rig.Vision.Reads);
    }

    [Fact]
    public void TheSpecSaysTheNotesAreRead()
    {
        var plan = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "CONSTITUTION-P13-PLAN.md"));
        Assert.Contains("only its four planner-note lines are read, once, from Sunday", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("the digest is never read", plan, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the file ----

    [Fact]
    public void TheFileRoundTripsAndComesFromTheThreeWeeksBefore()
    {
        var notes = new FilePlannerNotes(Path.Combine(folder, "planner-notes.json"));
        notes.Save("Juniper", Saturday, ["No fish"]);
        notes.Save("Juniper", Saturday.AddDays(-28), ["Too old"]);
        var again = new FilePlannerNotes(Path.Combine(folder, "planner-notes.json"));
        Assert.Equal(["No fish"], again.For(Saturday.AddDays(8)));
        Assert.Equal(["No fish"], again.For(Saturday.AddDays(1)));
        Assert.Empty(again.For(Saturday));
    }

    [Fact]
    public void ADamagedFileMeansNoNotesNotACrash()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "planner-notes.json"), "{ nope");
        var notes = new FilePlannerNotes(Path.Combine(folder, "planner-notes.json"));
        Assert.False(notes.Has("Juniper", Saturday));
        Assert.Empty(notes.For(Saturday.AddDays(8)));
    }

    // ---- the planner ----

    [Fact]
    public void NoNotesIsTodaysWeekByteForByte()
    {
        Assert.Equal(7827400281381623395UL, PlannerFingerprint.Of(seed => PlannerFingerprint.RequestFor(seed) with { PlannerNotes = [] }));
    }

    [Fact]
    public void NotesAreCarriedOntoTheWeekAndChangeNothingElse()
    {
        var request = PlannerFingerprint.RequestFor(3);
        var plain = MealPlanner.Plan(request);
        var noted = MealPlanner.Plan(request with { PlannerNotes = ["No fish this week"] });
        Assert.Contains(noted.Notes, note => note.Contains("No fish this week", StringComparison.Ordinal));
        Assert.Equal(plain.Dinners, noted.Dinners);
        Assert.NotEqual(MealWeekCodec.Write(plain), MealWeekCodec.Write(noted));
    }

    [Fact]
    public void TheRunAsksTheSeamForTheWeekItIsPlanning()
    {
        var run = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Infrastructure", "Meals", "MealWeekRun.cs"));
        Assert.Contains("seams.PlannerNotes", run, StringComparison.Ordinal);
        Assert.Contains("PlannerNotes = ", run, StringComparison.Ordinal);
    }
}
