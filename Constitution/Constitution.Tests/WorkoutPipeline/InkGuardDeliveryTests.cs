using Constitution.Domain.Delivery;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// S3: a page the owner has written on is never trashed or replaced by a blank one. The read-back reads a blank page as a skip, so a
/// blank copy set down over a begun session is a skip the owner never made. And a first send, which has no record of what is on the
/// tablet, looks the title up before it uploads: the cloud does not replace by title, so a second upload is a second document.
/// </summary>
public class InkGuardDeliveryTests
{
    private static readonly DateOnly Day = new(2026, 11, 3);
    private static readonly byte[] Pages = [1, 2, 3];
    private static readonly string Name = DeliveredName.For(DeliveryKind.Workout, Day, Sam);

    private static (WorkoutDocumentDelivery Delivery, FakeTablet Tablet, FakeInkProbe Probe) Make(InkProbe? answer = null)
    {
        var tablet = new FakeTablet();
        var probe = new FakeInkProbe { Answer = answer ?? InkProbe.Blank };
        var service = new DeliveryService(tablet, new Access(), () => Est(11, 3, 6));
        return (new WorkoutDocumentDelivery(service, tablet, probe), tablet, probe);
    }

    // ---- replacing ----

    [Fact]
    public async Task ABlankPageIsReplacedAfterTheLook()
    {
        var (delivery, tablet, probe) = Make();
        tablet.Titles["old-1"] = Name;

        var sent = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.True(sent.Delivered);
        Assert.Null(sent.Kept);
        Assert.Equal([Name], probe.Asked);
        Assert.Equal(["list", "trash:old-1", "upload:" + Name], tablet.Calls);
    }

    [Fact]
    public async Task AnInkedPageIsNotReplaced()
    {
        var (delivery, tablet, _) = Make(InkProbe.HasInk);
        tablet.Titles["old-1"] = Name;
        tablet.Pdfs["old-1"] = [9, 9];

        var sent = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.False(sent.Delivered);
        Assert.Equal(PageInk.HasInk, sent.Kept!.Ink);
        Assert.Equal(["list"], tablet.Calls);
        Assert.Equal([Name], tablet.Titles.Values);
        Assert.Equal([9, 9], tablet.Pdfs["old-1"]);
    }

    [Fact]
    public async Task APageThatCannotBeCheckedIsNotReplaced()
    {
        var failure = new HttpRequestException("503");
        var (delivery, tablet, _) = Make(InkProbe.Unknown(failure));
        tablet.Titles["old-1"] = Name;

        var sent = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.Equal(PageInk.Unknown, sent.Kept!.Ink);
        Assert.Same(failure, sent.Kept.Failure);
        Assert.Equal(["list"], tablet.Calls);
        Assert.Equal([Name], tablet.Titles.Values);
    }

    [Fact]
    public async Task TwoPagesUnderOneTitleCannotBothBeLookedAtSoNeitherGoes()
    {
        var (delivery, tablet, probe) = Make();
        tablet.Titles["old-1"] = Name;
        tablet.Titles["old-2"] = Name.ToUpperInvariant();

        var sent = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.Equal(PageInk.Unknown, sent.Kept!.Ink);
        Assert.Empty(probe.Asked);
        Assert.Equal(["list"], tablet.Calls);
        Assert.Equal(2, tablet.Titles.Count);
    }

    [Fact]
    public async Task NothingOnTheTabletNeedsNoLook()
    {
        var (delivery, tablet, probe) = Make(InkProbe.HasInk);

        var sent = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.True(sent.Delivered);
        Assert.Empty(probe.Asked);
        Assert.Equal(["list", "upload:" + Name], tablet.Calls);
    }

    [Fact]
    public async Task AnotherPersonsPageIsNotLookedAt()
    {
        var (delivery, tablet, probe) = Make(InkProbe.HasInk);
        tablet.Titles["other"] = DeliveredName.For(DeliveryKind.Workout, Day, Lee);

        var sent = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.True(sent.Delivered);
        Assert.Empty(probe.Asked);
    }

    // ---- withdrawing ----

    [Fact]
    public async Task ABlankPageIsWithdrawnAfterTheLook()
    {
        var (delivery, tablet, probe) = Make();
        tablet.Titles["old-1"] = Name;

        var withdrawn = await delivery.WithdrawAsync(Day, Sam);

        Assert.Null(withdrawn.Failure);
        Assert.Null(withdrawn.Kept);
        Assert.Equal([Name], probe.Asked);
        Assert.Equal(["list", "trash:old-1"], tablet.Calls);
    }

    [Fact]
    public async Task AnInkedPageIsNotWithdrawn()
    {
        var (delivery, tablet, _) = Make(InkProbe.HasInk);
        tablet.Titles["old-1"] = Name;

        var withdrawn = await delivery.WithdrawAsync(Day, Sam);

        Assert.Equal(PageInk.HasInk, withdrawn.Kept!.Ink);
        Assert.Null(withdrawn.Failure);
        Assert.Equal(["list"], tablet.Calls);
        Assert.Equal([Name], tablet.Titles.Values);
    }

    [Fact]
    public async Task APageThatCannotBeCheckedIsNotWithdrawn()
    {
        var (delivery, tablet, _) = Make(InkProbe.Unknown());
        tablet.Titles["old-1"] = Name;

        var withdrawn = await delivery.WithdrawAsync(Day, Sam);

        Assert.Equal(PageInk.Unknown, withdrawn.Kept!.Ink);
        Assert.Equal([Name], tablet.Titles.Values);
    }

    [Fact]
    public async Task APageAlreadyGoneIsWithdrawnWithoutALook()
    {
        var (delivery, tablet, probe) = Make(InkProbe.HasInk);

        var withdrawn = await delivery.WithdrawAsync(Day, Sam);

        Assert.Null(withdrawn.Failure);
        Assert.Null(withdrawn.Kept);
        Assert.Empty(probe.Asked);
        Assert.Equal(["list"], tablet.Calls);
    }

    // ---- a first send ----

    [Fact]
    public async Task AFirstSendLooksTheTitleUpBeforeItUploads()
    {
        var (delivery, tablet, probe) = Make();

        var sent = await delivery.SendAsync(Day, Sam, Pages, mayReplaceBlank: true);

        Assert.True(sent.Delivered);
        Assert.Equal(["list", "upload:" + Name], tablet.Calls);
        Assert.Empty(probe.Asked);
    }

    [Fact]
    public async Task AFirstSendNeverMakesASecondDocument()
    {
        // The record of what was delivered was lost, so the run sends as though for the first time; the page is still on the tablet.
        var (delivery, tablet, _) = Make();
        tablet.Titles["old-1"] = Name;

        var sent = await delivery.SendAsync(Day, Sam, Pages, mayReplaceBlank: true);

        Assert.True(sent.Delivered);
        Assert.Equal(["list", "trash:old-1", "upload:" + Name], tablet.Calls);
        Assert.Equal([Name], tablet.Titles.Values);
    }

    [Fact]
    public async Task AFirstSendOverAnInkedPageKeepsIt()
    {
        var (delivery, tablet, _) = Make(InkProbe.HasInk);
        tablet.Titles["old-1"] = Name;

        var sent = await delivery.SendAsync(Day, Sam, Pages, mayReplaceBlank: true);

        Assert.Equal(PageInk.HasInk, sent.Kept!.Ink);
        Assert.Equal(["list"], tablet.Calls);
        Assert.Equal([Name], tablet.Titles.Values);
    }

    [Fact]
    public async Task AFirstSendOverABlankPageOnceTheSlotHasBegunKeepsIt()
    {
        var (delivery, tablet, probe) = Make();
        tablet.Titles["old-1"] = Name;

        var sent = await delivery.SendAsync(Day, Sam, Pages, mayReplaceBlank: false);

        Assert.Equal(PageInk.Blank, sent.Kept!.Ink);
        Assert.Empty(probe.Asked);
        Assert.Equal(["list"], tablet.Calls);
        Assert.Equal([Name], tablet.Titles.Values);
    }

    [Fact]
    public async Task AFirstSendThatCannotLookTheTitleUpUploadsNothing()
    {
        var (delivery, tablet, _) = Make();
        tablet.ListFails = new HttpRequestException("503");

        var sent = await delivery.SendAsync(Day, Sam, Pages, mayReplaceBlank: true);

        Assert.False(sent.Delivered);
        Assert.IsType<HttpRequestException>(sent.Failure);
        Assert.Null(sent.Kept);
        Assert.Equal(["list"], tablet.Calls);
    }

    [Fact]
    public async Task AFirstSendWithOnlyAnotherPersonsPageUploads()
    {
        var (delivery, tablet, _) = Make(InkProbe.HasInk);
        tablet.Titles["other"] = DeliveredName.For(DeliveryKind.Workout, Day, Lee);

        var sent = await delivery.SendAsync(Day, Sam, Pages, mayReplaceBlank: true);

        Assert.True(sent.Delivered);
        Assert.Equal(2, tablet.Titles.Count);
    }
}
