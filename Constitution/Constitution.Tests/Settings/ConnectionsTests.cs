using Constitution.Domain.Areas;
using Constitution.Domain.Credentials;
using Constitution.Domain.Equipment;
using Constitution.Domain.Notices;
using Constitution.Domain.Settings;
using Storyworthtelling.Voice.Health;

namespace Constitution.Tests.Settings;

/// <summary>C-8.1.3: a working connection collapses; a broken one says what to do (book p.17).</summary>
public class ConnectionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 7, 30, 0, TimeSpan.Zero);

    private static SettingsModel Build(params ConnectionInput[] inputs) =>
        SettingsModel.Build(new Pillars(true, true, true), EquipmentSet.Of([]), inputs);

    [Fact]
    public void WorkingCollapses()
    {
        var model = Build(
            new ConnectionInput(SettingsModel.HealthConnectId, IsSet: true, ConnectionHealth.Working),
            new ConnectionInput(CredentialIds.SleepNumber, IsSet: true, ConnectionHealth.Working),
            new ConnectionInput(CredentialIds.Remarkable, IsSet: true, ConnectionHealth.Working));

        Assert.All(model.Connections, row =>
        {
            Assert.Equal(ConnectionStatus.Working, row.Status);
            Assert.Equal("Working ✓", row.StatusText);
            Assert.Null(row.ButtonLabel);
            Assert.False(row.IsProblem);
            Assert.Null(row.RaisesNotice);
        });
        Assert.Equal("Connections", model.ConnectionsHeader);
    }

    [Fact]
    public void BrokenShowsOneButtonAndRaisesANotice()
    {
        var model = Build(
            new ConnectionInput(SettingsModel.HealthConnectId, IsSet: true, ConnectionHealth.Working),
            new ConnectionInput(CredentialIds.SleepNumber, IsSet: true, ConnectionHealth.Rejected),
            new ConnectionInput(CredentialIds.Remarkable, IsSet: true, ConnectionHealth.Working));

        var broken = model.Connections.Single(row => row.CredentialId == CredentialIds.SleepNumber);
        Assert.Equal(ConnectionStatus.Problem, broken.Status);
        Assert.True(broken.IsProblem);
        Assert.False(string.IsNullOrWhiteSpace(broken.ButtonLabel));
        Assert.Contains("vault", broken.StatusText, StringComparison.Ordinal);
        Assert.Equal(NoticeKind.BedOffline, broken.RaisesNotice);
        Assert.Equal("Connections 1 needs you", model.ConnectionsHeader);

        var board = new NoticeBoard();
        Assert.True(SettingsModel.ApplyNotices(board, [
            new ConnectionInput(CredentialIds.SleepNumber, true, ConnectionHealth.Rejected)], Now));
        Assert.Equal(NoticeKind.BedOffline, Assert.Single(board.Waiting()).Kind);

        // The cause going takes the notice with it.
        Assert.True(SettingsModel.ApplyNotices(board, [
            new ConnectionInput(CredentialIds.SleepNumber, true, ConnectionHealth.Working)], Now));
        Assert.Empty(board.Waiting());
    }

    [Fact]
    public void NytCookingIsNotARow()
    {
        var model = Build();

        Assert.Equal(["Health Connect", "Sleep Number", "reMarkable"], model.Connections.Select(row => row.Name));
        Assert.DoesNotContain(model.Connections, row => row.Name.Contains("NYT", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, CredentialStatus.Ok)]
    [InlineData(true, CredentialStatus.Unknown)]
    [InlineData(true, CredentialStatus.Failing)]
    [InlineData(false, CredentialStatus.Ok)]
    [InlineData(false, CredentialStatus.Unknown)]
    [InlineData(false, CredentialStatus.Failing)]
    public void AgreesWithTheSharedCredentialFieldState(bool isSet, CredentialStatus status)
    {
        var health = status switch
        {
            CredentialStatus.Ok => CredentialHealth.Ok(Now),
            CredentialStatus.Failing => CredentialHealth.Failing("rejected", Now),
            _ => CredentialHealth.Unknown,
        };
        var shared = CredentialFieldState.Decide(isSet, health);
        var domainHealth = status switch
        {
            CredentialStatus.Ok => ConnectionHealth.Working,
            CredentialStatus.Failing => ConnectionHealth.Rejected,
            _ => ConnectionHealth.Unknown,
        };

        var row = Build(new ConnectionInput(CredentialIds.Remarkable, isSet, domainHealth)).Connections
            .Single(r => r.CredentialId == CredentialIds.Remarkable);

        Assert.Equal(shared.ShowEntry, row.EntryWanted);
        Assert.Equal(shared.IsWarning, row.IsProblem);
        if (!shared.IsWarning)
        {
            Assert.Equal(shared.StatusText, row.StatusText);
        }
    }
}
