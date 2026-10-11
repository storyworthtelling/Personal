using Pulp.Domain.Household;
using Pulp.Infrastructure.Household;

namespace Pulp.Tests.Infrastructure.Household;

/// <summary>The last role the vault said, kept on this install alone (never in the shared household file) so a cold start does not default to owner rights.</summary>
public sealed class LocalInstallRoleTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pulp-role-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ARoleSavedIsReadBackWithItsAccount()
    {
        var store = new LocalInstallStore(directory);
        store.SaveRole(HouseholdFileRole.SharedWith, "partner@example.com");

        var remembered = new LocalInstallStore(directory).Load().Role;

        Assert.Equal(new RememberedRole(HouseholdFileRole.SharedWith, "partner@example.com"), remembered);
    }

    [Fact]
    public void AFileWrittenBeforeRolesExistedReadsAsNoMemory()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, LocalInstallStore.FileName), "{\"installId\":\"install-a\",\"wall\":5,\"counter\":1}");

        Assert.Null(new LocalInstallStore(directory).Load().Role);
    }

    [Fact]
    public void SavingTheRoleKeepsTheWriterIdAndTheViewer()
    {
        var store = new LocalInstallStore(directory);
        var id = store.Load().InstallId;
        store.SaveViewer(new Pulp.Domain.Core.ViewerId("rowan"));
        store.SaveRole(HouseholdFileRole.Owner, "owner@example.com");

        var state = store.Load();

        Assert.Equal(id, state.InstallId);
        Assert.Equal("rowan", state.Viewer?.Value);
    }

    [Fact]
    public void SavingTheClockOrViewerAfterwardsKeepsTheRole()
    {
        var store = new LocalInstallStore(directory);
        store.SaveRole(HouseholdFileRole.Owner, "owner@example.com");
        store.SaveClock(new Pulp.Domain.Household.HlcTimestamp(9, 1));
        store.SaveViewer(null);

        Assert.Equal(HouseholdFileRole.Owner, store.Load().Role?.Role);
    }

    [Fact]
    public void UnknownOrAnonymousRolesAreRefusedNotStored()
    {
        var store = new LocalInstallStore(directory);

        Assert.Throws<ArgumentException>(() => store.SaveRole(HouseholdFileRole.Unknown, "owner@example.com"));
        Assert.Throws<ArgumentException>(() => store.SaveRole(HouseholdFileRole.Owner, " "));
    }

    [Fact]
    public void AnUnreadableRoleValueReadsAsNoMemoryNotAsADamagedInstall()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, LocalInstallStore.FileName),
            "{\"installId\":\"install-a\",\"wall\":5,\"counter\":1,\"role\":\"Emperor\",\"roleAccount\":\"x@example.com\"}");

        Assert.Null(new LocalInstallStore(directory).Load().Role);
    }
}
