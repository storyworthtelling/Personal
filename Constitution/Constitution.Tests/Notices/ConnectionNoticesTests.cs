using Constitution.Domain.Areas;
using Constitution.Domain.Credentials;
using Constitution.Domain.Equipment;
using Constitution.Domain.Notices;
using Constitution.Domain.Settings;

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

    // ---- a credential replaced by hand (S8) ----

    [Theory]
    [InlineData(CredentialIds.SleepNumber)]
    [InlineData(CredentialIds.Remarkable)]
    public void ReplacingACredentialTakesItsNoticeAway(string credentialId)
    {
        var board = new NoticeBoard();
        ConnectionNotices.Apply(board, credentialId, ConnectionHealth.Rejected, Start);

        var cleared = ConnectionNotices.Clear(board, credentialId);

        Assert.True(cleared);
        Assert.Empty(board.Waiting());
        Assert.Null(board.Fold(Start));
    }

    [Fact]
    public void ReplacingOneCredentialLeavesTheOthersNoticeWaiting()
    {
        var board = new NoticeBoard();
        ConnectionNotices.Apply(board, CredentialIds.SleepNumber, ConnectionHealth.Rejected, Start);
        ConnectionNotices.Apply(board, CredentialIds.Remarkable, ConnectionHealth.Rejected, Start);

        ConnectionNotices.Clear(board, CredentialIds.SleepNumber);

        Assert.Equal(NoticeKind.ReMarkableDelivery, Assert.Single(board.Waiting()).Kind);
    }

    [Theory]
    [InlineData(CredentialIds.SleepNumber)]
    [InlineData(CredentialIds.Claude)]
    [InlineData("something-unknown")]
    public void ReplacingACredentialWithNothingWaitingChangesNothing(string credentialId)
    {
        Assert.False(ConnectionNotices.Clear(new NoticeBoard(), credentialId));
    }

    private static readonly DateTimeOffset Judged = Start.AddHours(1);
    private static readonly DateTimeOffset Replaced = Start.AddHours(2);

    [Theory]
    [InlineData(ConnectionHealth.Rejected)]
    [InlineData(ConnectionHealth.Working)]
    public void AJudgementMadeBeforeTheCredentialWasReplacedSaysNothingAboutIt(ConnectionHealth stored)
    {
        Assert.Equal(ConnectionHealth.Unknown, ConnectionNotices.Judge(stored, Judged, Replaced));
    }

    [Fact]
    public void AJudgementMadeAtTheMomentOfReplacementIsStillTheOldCredentials()
    {
        Assert.Equal(ConnectionHealth.Unknown, ConnectionNotices.Judge(ConnectionHealth.Rejected, Replaced, Replaced));
    }

    [Fact]
    public void AJudgementMadeAfterTheReplacementStands()
    {
        Assert.Equal(ConnectionHealth.Rejected, ConnectionNotices.Judge(ConnectionHealth.Rejected, Replaced.AddMinutes(1), Replaced));
        Assert.Equal(ConnectionHealth.Working, ConnectionNotices.Judge(ConnectionHealth.Working, Replaced.AddMinutes(1), Replaced));
    }

    [Fact]
    public void AJudgementWithNoDateIsOlderThanAnyReplacement()
    {
        Assert.Equal(ConnectionHealth.Unknown, ConnectionNotices.Judge(ConnectionHealth.Rejected, null, Replaced));
    }

    [Theory]
    [InlineData(ConnectionHealth.Unknown)]
    [InlineData(ConnectionHealth.Working)]
    [InlineData(ConnectionHealth.Rejected)]
    public void ACredentialNeverReplacedByHandKeepsItsStoredJudgement(ConnectionHealth stored)
    {
        Assert.Equal(stored, ConnectionNotices.Judge(stored, Judged, savedAt: null));
    }

    [Fact]
    public void AReplacedCredentialStaysClearedWhenSettingsStatesItAgain()
    {
        // The whole path on the screen: the old failure is still in the report, the login was just saved.
        var board = new NoticeBoard();
        ConnectionNotices.Apply(board, CredentialIds.SleepNumber, ConnectionHealth.Rejected, Start);
        ConnectionNotices.Clear(board, CredentialIds.SleepNumber);

        var health = ConnectionNotices.Judge(ConnectionHealth.Rejected, Judged, Replaced);
        var input = new ConnectionInput(CredentialIds.SleepNumber, true, health);
        SettingsModel.ApplyNotices(board, [input], Replaced.AddMinutes(1));
        var row = SettingsModel
            .Build(new Pillars(true, true, true), EquipmentSet.Everything(), [input])
            .Connections.Single(connection => connection.CredentialId == CredentialIds.SleepNumber);

        Assert.Empty(board.Waiting());
        Assert.Equal(ConnectionStatus.Saved, row.Status);
    }

    [Theory]
    [InlineData(CredentialIds.SleepNumber, "credential-sleep-number-saved-at")]
    [InlineData(CredentialIds.Remarkable, "credential-remarkable-saved-at")]
    public void EachCredentialTypedOrPairedHereHasAFrozenSavedKey(string credentialId, string expected)
    {
        Assert.Equal(expected, CredentialSaves.KeyFor(credentialId));
    }

    [Theory]
    [InlineData(CredentialIds.Claude)]
    [InlineData(CredentialIds.Gemini)]
    [InlineData("something-unknown")]
    public void AKeyThatArrivesFromTheVaultHasNoSavedKey(string credentialId)
    {
        Assert.Null(CredentialSaves.KeyFor(credentialId));
    }
}
