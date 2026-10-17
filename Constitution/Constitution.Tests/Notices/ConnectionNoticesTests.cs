using Constitution.Domain.Credentials;
using Constitution.Domain.Notices;

namespace Constitution.Tests.Notices;

/// <summary>
/// A notice goes when its cause does, applied to connections: a rejected credential raises the notice
/// that belongs to it, and the credential working again takes it away. The Android head adapts the
/// shared credential-health report into <see cref="ConnectionHealth"/>; nothing here knows about it.
/// </summary>
public class ConnectionNoticesTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 14, 7, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CredentialIds.SleepNumber, NoticeKind.BedOffline)]
    [InlineData(CredentialIds.Remarkable, NoticeKind.ReMarkableDelivery)]
    public void ARejectedCredentialRaisesItsNotice(string credentialId, NoticeKind expected)
    {
        var board = new NoticeBoard();

        var changed = ConnectionNotices.Apply(board, credentialId, ConnectionHealth.Rejected, Start);

        Assert.True(changed);
        var notice = Assert.Single(board.Waiting());
        Assert.Equal(expected, notice.Kind);
        Assert.Equal(ConnectionNotices.CauseKey(credentialId), notice.CauseKey);
    }

    [Theory]
    [InlineData(CredentialIds.Claude)]
    [InlineData(CredentialIds.Gemini)]
    [InlineData("google-drive")]
    [InlineData("something-unknown")]
    public void AKeyWithNoNoticeRaisesNothing(string credentialId)
    {
        var board = new NoticeBoard();

        var changed = ConnectionNotices.Apply(board, credentialId, ConnectionHealth.Rejected, Start);

        Assert.False(changed);
        Assert.Empty(board.Waiting());
        Assert.Null(ConnectionNotices.KindFor(credentialId));
    }

    [Theory]
    [InlineData(CredentialIds.SleepNumber)]
    [InlineData(CredentialIds.Remarkable)]
    public void ANoticeGoesWhenItsCauseDoes(string credentialId)
    {
        var board = new NoticeBoard();
        ConnectionNotices.Apply(board, credentialId, ConnectionHealth.Rejected, Start);

        var changed = ConnectionNotices.Apply(board, credentialId, ConnectionHealth.Working, Start.AddMinutes(5));

        Assert.True(changed);
        Assert.Empty(board.Waiting());
        Assert.Null(board.Fold(Start.AddMinutes(5)));
    }

    [Fact]
    public void StatingARejectionAgainChangesNothingAndKeepsItsDate()
    {
        var board = new NoticeBoard();
        ConnectionNotices.Apply(board, CredentialIds.SleepNumber, ConnectionHealth.Rejected, Start);

        var changed = ConnectionNotices.Apply(board, CredentialIds.SleepNumber, ConnectionHealth.Rejected, Start.AddHours(2));

        Assert.False(changed);
        Assert.Equal(Start, Assert.Single(board.Waiting()).CreatedAt);
    }

    [Fact]
    public void WorkingWithNothingWaitingChangesNothing()
    {
        var board = new NoticeBoard();

        Assert.False(ConnectionNotices.Apply(board, CredentialIds.SleepNumber, ConnectionHealth.Working, Start));
    }

    [Fact]
    public void AnUnknownHealthLeavesAWaitingNoticeAlone()
    {
        // Restored from the vault and not yet used says nothing about whether the cause is gone.
        var board = new NoticeBoard();
        ConnectionNotices.Apply(board, CredentialIds.Remarkable, ConnectionHealth.Rejected, Start);

        var changed = ConnectionNotices.Apply(board, CredentialIds.Remarkable, ConnectionHealth.Unknown, Start.AddMinutes(1));

        Assert.False(changed);
        Assert.Single(board.Waiting());
    }

    [Fact]
    public void OneCredentialResolvingLeavesTheOtherWaiting()
    {
        var board = new NoticeBoard();
        ConnectionNotices.Apply(board, CredentialIds.SleepNumber, ConnectionHealth.Rejected, Start);
        ConnectionNotices.Apply(board, CredentialIds.Remarkable, ConnectionHealth.Rejected, Start.AddMinutes(1));

        ConnectionNotices.Apply(board, CredentialIds.SleepNumber, ConnectionHealth.Working, Start.AddMinutes(2));

        Assert.Equal(NoticeKind.ReMarkableDelivery, Assert.Single(board.Waiting()).Kind);
    }
}
