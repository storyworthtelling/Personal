using Constitution.Domain.Delivery;
using Constitution.Domain.Notices;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.Workouts;
using Storyworthtelling.Remarkable;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// Replacing a page "at the root" so that the tablet shows one document. The cloud does not replace by title: a second
/// upload under the same name is a second document, so the old one is taken off first and the new one sent after.
/// </summary>
public class WorkoutDocumentDeliveryTests
{
    private static readonly DateOnly Day = new(2026, 11, 3);
    private static readonly byte[] Pages = [1, 2, 3];
    private static readonly string Name = DeliveredName.For(DeliveryKind.Workout, Day, Sam);

    private static (WorkoutDocumentDelivery Delivery, FakeTablet Tablet, Access Access) Make()
    {
        var tablet = new FakeTablet();
        var access = new Access();
        var service = new DeliveryService(tablet, access, () => Est(11, 3, 6));
        return (new WorkoutDocumentDelivery(service, tablet), tablet, access);
    }

    [Fact]
    public async Task ASendUploadsAtTheRootAndAsksNothing()
    {
        var (delivery, tablet, _) = Make();

        var result = await delivery.SendAsync(Day, Sam, Pages);

        Assert.True(result.Delivered);
        Assert.Equal(Name, result.Name);
        Assert.Equal(["upload:" + Name], tablet.Calls);
    }

    [Fact]
    public async Task TheOldPageGoesBeforeTheNewOne()
    {
        var (delivery, tablet, _) = Make();
        tablet.Titles["old-1"] = Name;

        var result = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.True(result.Delivered);
        Assert.Equal(["list", "trash:old-1", "upload:" + Name], tablet.Calls);
        Assert.Equal([Name], tablet.Titles.Values);
    }

    [Fact]
    public async Task ReplacementTrashesEveryPageOfThatTitle()
    {
        var (delivery, tablet, _) = Make();
        tablet.Titles["old-1"] = Name;
        tablet.Titles["old-2"] = Name.ToUpperInvariant();
        tablet.Titles["other"] = DeliveredName.For(DeliveryKind.Workout, Day, Lee);
        tablet.Titles["digest"] = DeliveredName.For(DeliveryKind.Digest, Day);

        await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.Contains("trash:old-1", tablet.Calls);
        Assert.Contains("trash:old-2", tablet.Calls);
        Assert.DoesNotContain("trash:other", tablet.Calls);
        Assert.DoesNotContain("trash:digest", tablet.Calls);
        Assert.Equal(3, tablet.Titles.Count);
    }

    [Fact]
    public async Task AReplacementWithNothingOnTheTabletJustUploads()
    {
        var (delivery, tablet, _) = Make();

        var result = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.True(result.Delivered);
        Assert.Equal(["list", "upload:" + Name], tablet.Calls);
    }

    [Fact]
    public async Task ABlockedTrashMeansNothingIsSent()
    {
        var (delivery, tablet, _) = Make();
        tablet.Titles["old-1"] = Name;
        tablet.TrashFails = new HttpRequestException("503");

        var result = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.False(result.Delivered);
        Assert.IsType<HttpRequestException>(result.Failure);
        Assert.DoesNotContain("upload:" + Name, tablet.Calls);
        Assert.Equal([Name], tablet.Titles.Values);
    }

    [Fact]
    public async Task ATabletThatCannotBeListedMeansNothingIsSent()
    {
        var (delivery, tablet, _) = Make();
        tablet.ListFails = new HttpRequestException("503");

        var result = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.False(result.Delivered);
        Assert.Equal(["list"], tablet.Calls);
    }

    [Fact]
    public async Task AFailedUploadAfterTheTrashRaisesTheNotice()
    {
        var (delivery, tablet, access) = Make();
        tablet.Titles["old-1"] = Name;
        tablet.UploadFails = new HttpRequestException("503");

        var result = await delivery.ReplaceAsync(Day, Sam, Pages);

        Assert.False(result.Delivered);
        Assert.Equal(NoticeKind.ReMarkableDelivery, Assert.Single(access.Board.Waiting()).Kind);
        Assert.Empty(tablet.Titles);
    }

    [Fact]
    public async Task OnlyNetworkFailuresAreReported()
    {
        var (delivery, tablet, _) = Make();
        tablet.Titles["old-1"] = Name;
        tablet.TrashFails = new InvalidOperationException("a bug");

        await Assert.ThrowsAsync<InvalidOperationException>(() => delivery.ReplaceAsync(Day, Sam, Pages));
    }

    [Fact]
    public async Task AWithdrawalTakesThePageOffAndSendsNothing()
    {
        var (delivery, tablet, _) = Make();
        tablet.Titles["old-1"] = Name;

        var failure = await delivery.WithdrawAsync(Day, Sam);

        Assert.Null(failure);
        Assert.Equal(["list", "trash:old-1"], tablet.Calls);
        Assert.Empty(tablet.Titles);
    }

    [Fact]
    public async Task AWithdrawalOfAPageThatIsAlreadyGoneIsDone()
    {
        var (delivery, tablet, _) = Make();

        Assert.Null(await delivery.WithdrawAsync(Day, Sam));
        Assert.Equal(["list"], tablet.Calls);
    }

    [Fact]
    public async Task AWithdrawalThatFailsSaysSoAndKeepsTheException()
    {
        var (delivery, tablet, _) = Make();
        tablet.Titles["old-1"] = Name;
        tablet.TrashFails = new HttpRequestException("503");

        var failure = await delivery.WithdrawAsync(Day, Sam);

        Assert.IsType<HttpRequestException>(failure);
    }
}
