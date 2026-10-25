using Constitution.Domain.Delivery;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// S3: the look at a page. It downloads what the tablet holds under the title and has the vision say whether there is ink on any box the
/// document asked about. Only a page that was read through, and that says it is unmarked, from a cloud whose copy is known to carry pen
/// ink at all, is Blank; every doubt is Unknown, and Unknown keeps the page.
/// </summary>
public sealed class VisionPageInkProbeTests : IDisposable
{
    private readonly WeekRig rig = new();

    public void Dispose() => rig.Dispose();

    private static readonly Session Cardio = new(SessionKind.OutdoorCardio, TimeSpan.FromMinutes(35), true, [new("Warm-up walk"), new("Brisk walk"), new("Cool-down walk")]);

    private static readonly string Name = DeliveredName.For(DeliveryKind.Workout, MondayDay(0), Sam);

    private sealed class Vision(Func<ExpectedDocument, InkDocumentRead> answer) : IInkVision
    {
        public int Reads { get; private set; }

        public Task<InkDocumentRead> Read(ExpectedDocument document, byte[] annotatedPdf, CancellationToken cancellation)
        {
            Reads++;
            return Task.FromResult(answer(document));
        }
    }

    private static InkDocumentRead Pages(ExpectedDocument document, PenStrokes strokes, bool markFirst = false)
    {
        var first = true;
        var pages = document.Pages.ToDictionary(
            page => page.Number,
            page => InkPageRead.Read(page.AskedTargets.Select(target =>
            {
                var marked = markFirst && first;
                first = false;
                return new InkTargetRead(target.Id, marked, 0.95);
            })));
        return new InkDocumentRead(pages, strokes);
    }

    private async Task<VisionPageInkProbe> TheProbe(Vision vision, bool proven = true)
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio)));
        await rig.PlannedAsync();
        Assert.Single(rig.Tablet.Titles);
        return new VisionPageInkProbe(new RemarkableAnnotatedPageSource(rig.Tablet), vision, rig.Expected, () => proven);
    }

    [Fact]
    public async Task APageNothingWasWrittenOnIsBlank()
    {
        var vision = new Vision(document => Pages(document, PenStrokes.None));

        var found = await (await TheProbe(vision)).ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.Blank, found.Ink);
        Assert.Equal(1, vision.Reads);
        Assert.Contains("download:" + Name, rig.Tablet.Calls);
    }

    [Fact]
    public async Task AMarkedBoxIsInkWhateverThePageSays()
    {
        var vision = new Vision(document => Pages(document, PenStrokes.None, markFirst: true));

        var found = await (await TheProbe(vision)).ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.HasInk, found.Ink);
    }

    [Fact]
    public async Task PenInkSeenAnywhereOnThePageIsInkEvenInNoBox()
    {
        var vision = new Vision(document => Pages(document, PenStrokes.Seen));

        var found = await (await TheProbe(vision)).ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.HasInk, found.Ink);
    }

    [Fact]
    public async Task InkIsInkEvenBeforeTheCloudHasEverShownPenInk()
    {
        var vision = new Vision(document => Pages(document, PenStrokes.Seen));

        var found = await (await TheProbe(vision, proven: false)).ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.HasInk, found.Ink);
    }

    [Fact]
    public async Task AnUnmarkedPageIsNotBlankWhileThePenIsStillToBeProvenToComeThrough()
    {
        var vision = new Vision(document => Pages(document, PenStrokes.None));

        var found = await (await TheProbe(vision, proven: false)).ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.Unknown, found.Ink);
        Assert.Null(found.Failure);
    }

    [Fact]
    public async Task AReadThatDidNotSayAboutPenInkIsNotBlank()
    {
        var vision = new Vision(document => Pages(document, PenStrokes.Unknown));

        var found = await (await TheProbe(vision)).ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.Unknown, found.Ink);
    }

    [Fact]
    public async Task APageThatWasNotReadIsNotBlank()
    {
        var vision = new Vision(document => new InkDocumentRead(document.Pages.ToDictionary(page => page.Number, _ => InkPageRead.NotRead("The reply made no sense.")), PenStrokes.None));

        var found = await (await TheProbe(vision)).ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.Unknown, found.Ink);
    }

    [Fact]
    public async Task AReadThatFailedIsUnknownAndKeepsTheFailure()
    {
        var failure = new HttpRequestException("503");
        var vision = new Vision(_ => throw failure);

        var found = await (await TheProbe(vision)).ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.Unknown, found.Ink);
        Assert.Same(failure, found.Failure);
    }

    [Fact]
    public async Task AMissingKeyIsUnknownNotABug()
    {
        var vision = new Vision(_ => throw new InvalidOperationException("The Claude key is not set."));

        var found = await (await TheProbe(vision)).ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.Unknown, found.Ink);
        Assert.IsType<InvalidOperationException>(found.Failure);
    }

    [Fact]
    public async Task ADownloadThatFailedIsUnknown()
    {
        var vision = new Vision(document => Pages(document, PenStrokes.None));
        var probe = new VisionPageInkProbe(new FailingSource(new HttpRequestException("503")), vision, rig.Expected, () => true);

        var found = await probe.ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.Unknown, found.Ink);
        Assert.IsType<HttpRequestException>(found.Failure);
        Assert.Equal(0, vision.Reads);
    }

    [Fact]
    public async Task APageThatIsNotOnTheTabletHasNothingToLose()
    {
        var vision = new Vision(document => Pages(document, PenStrokes.Seen));
        var probe = await TheProbe(vision);
        rig.Tablet.Titles.Clear();
        rig.Tablet.Pdfs.Clear();

        var found = await probe.ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.Blank, found.Ink);
        Assert.Equal(0, vision.Reads);
    }

    [Fact]
    public async Task APageNobodyIsWaitingToReadCannotBeAskedAbout()
    {
        var vision = new Vision(document => Pages(document, PenStrokes.None));
        var probe = await TheProbe(vision);
        rig.Expected.Retire(Name);

        var found = await probe.ProbeAsync(Name, CancellationToken.None);

        Assert.Equal(PageInk.Unknown, found.Ink);
        Assert.Equal(0, vision.Reads);
    }

    [Fact]
    public async Task ACancelledLookIsCancelledNotUnknown()
    {
        using var source = new CancellationTokenSource();
        var vision = new Vision(_ =>
        {
            source.Cancel();
            throw new OperationCanceledException(source.Token);
        });
        var probe = await TheProbe(vision);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.ProbeAsync(Name, source.Token));
    }

    [Fact]
    public async Task ABugIsNotMistakenForADoubt()
    {
        var vision = new Vision(_ => throw new NullReferenceException());
        var probe = await TheProbe(vision);

        await Assert.ThrowsAsync<NullReferenceException>(() => probe.ProbeAsync(Name, CancellationToken.None));
    }

    private sealed class FailingSource(Exception failure) : IAnnotatedPageSource
    {
        public Task<TabletDocument?> Download(string title) => throw failure;

        public Task<string?> Locate(string title) => throw failure;

        public Task Trash(string documentId) => throw failure;
    }
}
