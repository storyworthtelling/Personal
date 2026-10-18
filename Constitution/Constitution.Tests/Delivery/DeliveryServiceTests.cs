using System.Net;
using Constitution.Domain.Delivery;
using Constitution.Domain.Notices;
using Constitution.Infrastructure.Delivery;
using Moq;
using Storyworthtelling.Remarkable;

namespace Constitution.Tests.Delivery;

/// <summary>C-2.2.2 — delivery to the tablet's root, and what a failure leaves behind (book p.18; D85).</summary>
public class DeliveryServiceTests
{
    private static readonly DateOnly Day = new(2026, 3, 14);
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 5, 30, 0, TimeSpan.Zero);
    private static readonly byte[] Pages = [1, 2, 3, 4];

    private sealed class Access : INoticeBoardAccess
    {
        public NoticeBoard Board { get; } = new();

        public T With<T>(Func<NoticeBoard, T> use) => use(Board);
    }

    private static HttpRequestException Refused() =>
        new("Failed to obtain reMarkable user token: 401 (Unauthorized)", null, HttpStatusCode.Unauthorized);

    private static (DeliveryService Service, Mock<IRemarkableClient> Client, Access Access) Make()
    {
        var client = new Mock<IRemarkableClient>(MockBehavior.Strict);
        var access = new Access();
        return (new DeliveryService(client.Object, access, () => Now), client, access);
    }

    [Fact]
    public async Task UploadsAtTheRoot()
    {
        var (service, client, access) = Make();
        var name = DeliveredName.For(DeliveryKind.Workout, Day);
        client.Setup(c => c.Upload(name, Pages)).Returns(Task.CompletedTask);

        var result = await service.Deliver(DeliveryKind.Workout, Day, null, Pages);

        Assert.True(result.Delivered);
        Assert.Equal(name, result.Name);
        Assert.Null(result.Failure);
        client.Verify(c => c.Upload(name, Pages), Times.Once);
        client.Verify(c => c.Upload(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>()), Times.Never);
        client.Verify(c => c.EnsureFolder(It.IsAny<string>()), Times.Never);
        Assert.Empty(access.Board.Waiting());
        Assert.Equal(0, service.PendingCount);
    }

    [Fact]
    public async Task RefusedTokenRaisesTheProblemNotice()
    {
        var (service, client, access) = Make();
        client.Setup(c => c.Upload(It.IsAny<string>(), It.IsAny<byte[]>())).ThrowsAsync(Refused());

        var result = await service.Deliver(DeliveryKind.Workout, Day, null, Pages);

        Assert.False(result.Delivered);
        Assert.IsType<HttpRequestException>(result.Failure);
        var notice = Assert.Single(access.Board.Waiting());
        Assert.Equal(NoticeKind.ReMarkableDelivery, notice.Kind);
        Assert.Equal(Codex.Domain.Design.FoldRole.Problem, notice.Role);
        Assert.Equal(Now, notice.CreatedAt);
        Assert.Equal(ConnectionNotices.CauseKey("remarkable"), notice.CauseKey);
        Assert.Equal(1, service.PendingCount);
    }

    [Fact]
    public async Task AFailedUploadRaisesTheSameNotice()
    {
        var (service, client, access) = Make();
        client.Setup(c => c.Upload(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ThrowsAsync(new HttpRequestException("reMarkable upload failed: 503", null, HttpStatusCode.ServiceUnavailable));

        await service.Deliver(DeliveryKind.Meals, Day, null, Pages);

        Assert.Equal(NoticeKind.ReMarkableDelivery, Assert.Single(access.Board.Waiting()).Kind);
    }

    [Fact]
    public async Task RetriesAfterThePairingIsFixed()
    {
        var (service, client, access) = Make();
        var name = DeliveredName.For(DeliveryKind.Workout, Day);
        client.SetupSequence(c => c.Upload(name, Pages))
            .ThrowsAsync(Refused())
            .Returns(Task.CompletedTask);
        await service.Deliver(DeliveryKind.Workout, Day, null, Pages);

        var delivered = await service.RetryPending();

        Assert.Equal(1, delivered);
        Assert.Equal(0, service.PendingCount);
        Assert.Empty(access.Board.Waiting());
        client.Verify(c => c.Upload(name, Pages), Times.Exactly(2));
    }

    [Fact]
    public async Task AStillRefusedRetryKeepsTheNoticeOnce()
    {
        var (service, client, access) = Make();
        client.Setup(c => c.Upload(It.IsAny<string>(), It.IsAny<byte[]>())).ThrowsAsync(Refused());
        await service.Deliver(DeliveryKind.Workout, Day, null, Pages);

        var delivered = await service.RetryPending();

        Assert.Equal(0, delivered);
        Assert.Equal(1, service.PendingCount);
        Assert.Single(access.Board.Waiting());
    }

    [Fact]
    public async Task OneStillWaitingKeepsTheNotice()
    {
        var (service, client, access) = Make();
        var workout = DeliveredName.For(DeliveryKind.Workout, Day);
        client.Setup(c => c.Upload(It.IsAny<string>(), It.IsAny<byte[]>())).ThrowsAsync(Refused());
        await service.Deliver(DeliveryKind.Workout, Day, null, Pages);
        await service.Deliver(DeliveryKind.Meals, Day, null, Pages);
        client.Reset();
        client.Setup(c => c.Upload(workout, Pages)).Returns(Task.CompletedTask);
        client.Setup(c => c.Upload(DeliveredName.For(DeliveryKind.Meals, Day), Pages)).ThrowsAsync(Refused());

        var delivered = await service.RetryPending();

        Assert.Equal(1, delivered);
        Assert.Equal(1, service.PendingCount);
        Assert.Single(access.Board.Waiting());
    }

    [Fact]
    public async Task AResendIsNotQueuedTwice()
    {
        var (service, client, _) = Make();
        client.Setup(c => c.Upload(It.IsAny<string>(), It.IsAny<byte[]>())).ThrowsAsync(Refused());

        await service.Deliver(DeliveryKind.Workout, Day, null, Pages);
        await service.Deliver(DeliveryKind.Workout, Day, null, [9, 9]);

        Assert.Equal(1, service.PendingCount);
    }

    [Fact]
    public async Task ADeliveryDoesNotClearAWaitingNotice()
    {
        var (service, client, access) = Make();
        client.SetupSequence(c => c.Upload(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ThrowsAsync(Refused())
            .Returns(Task.CompletedTask);
        await service.Deliver(DeliveryKind.Workout, Day, null, Pages);

        await service.Deliver(DeliveryKind.Meals, Day, null, Pages);

        Assert.Equal(1, service.PendingCount);
        Assert.Single(access.Board.Waiting());
    }

    [Fact]
    public async Task NeverFallsBackToThePhone()
    {
        var (service, client, _) = Make();
        client.Setup(c => c.Upload(It.IsAny<string>(), It.IsAny<byte[]>())).ThrowsAsync(Refused());

        await service.Deliver(DeliveryKind.Workout, Day, null, Pages);
        await service.RetryPending();

        // Nothing is offered to a share sheet, a viewer or a folder, and the source holds no file API at all.
        client.Verify(c => c.Upload(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>()), Times.Never);
        var forbidden = new[] { "File.", "Directory.", "FileStream", "StreamWriter", "Path.GetTempPath", "Intent", "MediaStore", "Environment.GetFolderPath" };
        var folders = new[] { "Constitution.Domain/Delivery", "Constitution.Infrastructure/Delivery" };
        var sources = ConstitutionSource.Files(".cs").Where(f => folders.Any(d => ConstitutionSource.Relative(f).StartsWith(d + "/", StringComparison.Ordinal))).ToList();
        Assert.NotEmpty(sources);
        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            foreach (var word in forbidden)
            {
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{ConstitutionSource.Relative(source)} names {word}");
            }
        }
    }

    [Fact]
    public async Task ACancelledDeliveryIsNotAFailure()
    {
        var (service, client, access) = Make();
        client.Setup(c => c.Upload(It.IsAny<string>(), It.IsAny<byte[]>())).ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.Deliver(DeliveryKind.Workout, Day, null, Pages));

        Assert.Empty(access.Board.Waiting());
    }
}
