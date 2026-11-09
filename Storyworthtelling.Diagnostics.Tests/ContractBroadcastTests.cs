using Storyworthtelling.Diagnostics;
using Xunit;

namespace Storyworthtelling.Diagnostics.Tests;

/// <summary>
/// What a cross-app broadcast says about itself (audit W2, 09 L1 and L2). SendBroadcast reports nothing, which is
/// how every contract stayed broken without anyone seeing it: so a courier checks before it sends and logs what
/// happened, a receiver logs what arrived, and every broadcast carries a version so a change of meaning can be told
/// apart from an old sender. No line ever carries the payload.
/// </summary>
public class ContractBroadcastTests
{
    [Fact]
    public void ACourierSendsOnlyToAnInstalledReceiverItHoldsThePermissionFor()
    {
        Assert.Equal(ContractSendOutcome.Sent, ContractBroadcast.Before(receiverInstalled: true, holdsPermission: true));
        Assert.Equal(ContractSendOutcome.ReceiverAbsent, ContractBroadcast.Before(receiverInstalled: false, holdsPermission: true));
        Assert.Equal(ContractSendOutcome.ReceiverAbsent, ContractBroadcast.Before(receiverInstalled: false, holdsPermission: false));
        Assert.Equal(ContractSendOutcome.NotPermitted, ContractBroadcast.Before(receiverInstalled: true, holdsPermission: false));
    }

    [Fact]
    public void EachOutcomeHasItsOwnLineNamingTheContractAndNothingElse()
    {
        var lines = Enum.GetValues<ContractSendOutcome>()
            .Select(outcome => ContractBroadcast.SendLine("hidden-apps", outcome, "SecurityException"))
            .ToList();

        Assert.Equal(lines.Count, lines.Distinct().Count());
        Assert.All(lines, line => Assert.StartsWith("CONTRACT hidden-apps: ", line, StringComparison.Ordinal));
        Assert.Equal("CONTRACT hidden-apps: sent v1", ContractBroadcast.SendLine("hidden-apps", ContractSendOutcome.Sent));
        Assert.Contains("SecurityException", ContractBroadcast.SendLine("hidden-apps", ContractSendOutcome.Failed, "SecurityException"), StringComparison.Ordinal);
    }

    [Fact]
    public void ABroadcastWithNoVersionCameFromAnOlderSenderAndIsVersionOne()
    {
        Assert.Equal("v", ContractBroadcast.VersionExtra);
        Assert.Equal(1, ContractBroadcast.Version);
        Assert.Equal(1, ContractBroadcast.ReadVersion(0));
        Assert.Equal(1, ContractBroadcast.ReadVersion(1));
        Assert.Equal(2, ContractBroadcast.ReadVersion(2));
    }

    [Fact]
    public void AReceiverRefusesAVersionNewerThanItReads()
    {
        // A newer sender may mean something else by the same keys; reading it as v1 would be a guess.
        Assert.True(ContractBroadcast.Accepts(1));
        Assert.False(ContractBroadcast.Accepts(2));

        Assert.Equal("CONTRACT budget: received v1", ContractBroadcast.ReceiptLine("budget", 1));
        Assert.Equal("CONTRACT budget: refused v2, newer than this app reads; update it", ContractBroadcast.ReceiptLine("budget", 2));
    }
}
