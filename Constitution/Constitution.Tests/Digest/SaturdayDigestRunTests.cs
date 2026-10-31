using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Notices;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.Documents;
using Constitution.Infrastructure.ReadBack;
using Constitution.Tests.WorkoutPipeline;
using UglyToad.PdfPig;

namespace Constitution.Tests.DigestPages;

/// <summary>
/// Jobs 2 and 3 — the digest is drawn to a vector PDF through the shared composer, delivered once per week through the reMarkable seam, recorded so the read-back
/// trashes it three days on, and retried (not duplicated) when something stops it.
/// </summary>
public class SaturdayDigestRunTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-5), "test", "test");
    private static readonly DateTimeOffset SaturdayMorning = new(2026, 3, 14, 6, 0, 0, TimeSpan.FromHours(-5));

    private sealed class Rig
    {
        public FakeTablet Tablet { get; } = new();

        public Access Notices { get; } = new();

        public FakeSources Sources { get; } = new();

        public MemoryStamps Stamps { get; } = new();

        public FakeExpected Expected { get; } = new();

        public SaturdayDigestRun Run() =>
            new(Sources, new DeliveryService(Tablet, Notices, () => SaturdayMorning), Expected, Stamps);
    }

    private sealed class FakeExpected : IExpectedDocuments
    {
        public List<ExpectedDocument> Recorded { get; } = [];

        public IReadOnlyList<OutstandingDocument> Outstanding() => Recorded.Select(item => new OutstandingDocument(item, false)).ToList();

        public void Record(ExpectedDocument document) => Recorded.Add(document);

        public void MarkSaved(string documentName)
        {
        }

        public void Retire(string documentName)
        {
        }
    }

    [Fact]
    public async Task DeliversTheDigestOncePerWeek()
    {
        var rig = new Rig();
        var first = await rig.Run().RunAsync(SaturdayMorning, Zone, "Sam", CancellationToken.None);
        var delivered = Assert.IsType<DigestOutcome.Delivered>(first);
        Assert.Equal(DeliveredName.For(DeliveryKind.Digest, DigestFixture.Saturday), delivered.Name);
        Assert.Equal(1, rig.Tablet.Uploads);

        var second = await rig.Run().RunAsync(SaturdayMorning.AddHours(3), Zone, "Sam", CancellationToken.None);
        Assert.IsType<DigestOutcome.AlreadyDelivered>(second);
        Assert.Equal(1, rig.Tablet.Uploads);
    }

    [Fact]
    public async Task NotDueTouchesNothing()
    {
        var rig = new Rig();
        var outcome = await rig.Run().RunAsync(SaturdayMorning.AddDays(-1), Zone, "Sam", CancellationToken.None);
        Assert.IsType<DigestOutcome.NotDue>(outcome);
        Assert.Empty(rig.Sources.Calls);
        Assert.Equal(0, rig.Tablet.Uploads);
    }

    [Fact]
    public async Task ATomorrowCatchUpDeliversAMissedSaturdayUnderItsOwnDate()
    {
        var rig = new Rig();
        var outcome = await rig.Run().RunAsync(SaturdayMorning.AddDays(1), Zone, "Sam", CancellationToken.None);
        Assert.Equal(DeliveredName.For(DeliveryKind.Digest, DigestFixture.Saturday), Assert.IsType<DigestOutcome.Delivered>(outcome).Name);
    }

    [Fact]
    public async Task AStoreThatCannotAnswerSendsNothingAndStampsNothing()
    {
        var rig = new Rig();
        rig.Sources.Unavailable = "The skip ledger could not be read.";
        var outcome = await rig.Run().RunAsync(SaturdayMorning, Zone, "Sam", CancellationToken.None);
        Assert.Equal("The skip ledger could not be read.", Assert.IsType<DigestOutcome.NotReady>(outcome).Reason);
        Assert.Equal(0, rig.Tablet.Uploads);
        Assert.False(rig.Stamps.Has("Sam", DigestFixture.Saturday));
        Assert.Empty(rig.Expected.Recorded);

        rig.Sources.Unavailable = null;
        Assert.IsType<DigestOutcome.Delivered>(await rig.Run().RunAsync(SaturdayMorning.AddHours(1), Zone, "Sam", CancellationToken.None));
    }

    [Fact]
    public async Task AFailedUploadIsNotStampedAndRaisesTheDeliveryNotice()
    {
        var rig = new Rig();
        rig.Tablet.UploadFails = new HttpRequestException("refused");
        var outcome = await rig.Run().RunAsync(SaturdayMorning, Zone, "Sam", CancellationToken.None);
        Assert.IsType<DigestOutcome.NotDelivered>(outcome);
        Assert.False(rig.Stamps.Has("Sam", DigestFixture.Saturday));
        Assert.Empty(rig.Expected.Recorded);
        Assert.Contains(rig.Notices.Board.Waiting(), notice => notice.Kind == NoticeKind.ReMarkableDelivery);

        rig.Tablet.UploadFails = null;
        Assert.IsType<DigestOutcome.Delivered>(await rig.Run().RunAsync(SaturdayMorning.AddHours(1), Zone, "Sam", CancellationToken.None));
        Assert.DoesNotContain(rig.Notices.Board.Waiting(), notice => notice.Kind == NoticeKind.ReMarkableDelivery);
    }

    [Fact]
    public async Task EachAdultHasTheirOwnStamp()
    {
        var rig = new Rig();
        await rig.Run().RunAsync(SaturdayMorning, Zone, "Sam", CancellationToken.None);
        Assert.IsType<DigestOutcome.Delivered>(await rig.Run().RunAsync(SaturdayMorning, Zone, "Lee", CancellationToken.None));
        Assert.Equal(2, rig.Tablet.Uploads);
    }

    [Fact]
    public async Task IsRecordedSoTheReadBackTrashesItAfterThreeDays()
    {
        var rig = new Rig();
        var delivered = Assert.IsType<DigestOutcome.Delivered>(await rig.Run().RunAsync(SaturdayMorning, Zone, "Sam", CancellationToken.None));
        var recorded = Assert.Single(rig.Expected.Recorded);
        Assert.Equal(DeliveryKind.Digest, recorded.Kind);
        Assert.Equal(DigestFixture.Saturday, recorded.Day);
        Assert.Equal("Sam", recorded.Person);
        Assert.Equal(delivered.Name, recorded.Name);
        Assert.True(recorded.ReadsPlannerNotes);
        Assert.Single(recorded.Pages);
        Assert.False(ReadBackPolicy.DigestIsDue(recorded.Day, DigestFixture.Saturday.AddDays(2)));
        Assert.True(ReadBackPolicy.DigestIsDue(recorded.Day, DigestFixture.Saturday.AddDays(3)));
    }

    // ---- drawing ----

    [Fact]
    public void TheRenderedPdfHasOnePagePerModelPageAndExtractableText()
    {
        var pages = SaturdayDigest.Build(DigestFixture.Model());
        var pdf = ConstitutionDocumentRenderer.Render(pages);
        using var document = PdfDocument.Open(pdf);
        Assert.Equal(pages.Count, document.NumberOfPages);
        foreach (var (layout, page) in pages.Zip(document.GetPages()))
        {
            var text = page.Text;
            Assert.Contains(layout.Title, text, StringComparison.Ordinal);
            foreach (var item in layout.Printed.Where(item => item.Role is PrintedRole.Body or PrintedRole.Strong or PrintedRole.Mono && !string.IsNullOrEmpty(item.Text)))
            {
                Assert.Contains(item.Text!.Replace(" ", ""), text.Replace(" ", ""), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void EveryDigestPageIsPaintable()
    {
        // The painter refuses a page that breaks the ink-target rules; the digest's planner lines must clear everything printed.
        foreach (var page in SaturdayDigest.Build(DigestFixture.Model()))
        {
            Assert.Empty(page.Violations());
        }
    }

    [Fact]
    public void ADrawnDigestIsTheSameBytesTwice()
    {
        var model = DigestFixture.Model();
        Assert.Equal(
            ConstitutionDocumentRenderer.Render(SaturdayDigest.Build(model)),
            ConstitutionDocumentRenderer.Render(SaturdayDigest.Build(model)));
    }

    [Fact]
    public void TheChartsAndSquaresAreInTheLayoutAsDrawableItems()
    {
        var pages = SaturdayDigest.Build(DigestFixture.Model());
        var charts = pages.SelectMany(page => page.Printed).Where(item => item.Role == PrintedRole.Chart).ToList();
        Assert.Equal(9, charts.Count);
        Assert.All(charts, chart => Assert.Equal(12, chart.Points!.Count));
        var squares = pages[0].Printed.Where(item => item.Role is PrintedRole.MarkFilled or PrintedRole.MarkOpen).ToList();
        Assert.Equal(3, squares.Count(item => item.Role == PrintedRole.MarkFilled));
        Assert.Equal(1, squares.Count(item => item.Role == PrintedRole.MarkOpen));
    }

    [Fact]
    public void ADrawnChartChangesThePage()
    {
        var flat = SaturdayDigest.Build(DigestFixture.Model(readings: DigestFixture.Flat(Constitution.Domain.Body.BodySignal.Sleep, 7.0)));
        var none = SaturdayDigest.Build(DigestFixture.Model(readings: []));
        Assert.NotEqual(ConstitutionDocumentRenderer.Render(flat), ConstitutionDocumentRenderer.Render(none));
    }
}
