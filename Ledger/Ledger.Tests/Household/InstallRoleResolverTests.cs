using Index.Domain.Household;
using Xunit;

namespace Index.Tests.Household;

public class InstallRoleResolverTests
{
    [Fact]
    public void Propose_ReturnsNull_WhenTheSignedInAccountIsUnknown()
    {
        Assert.Null(InstallRoleResolver.Propose(null, "owner@example.com"));
        Assert.Null(InstallRoleResolver.Propose(string.Empty, "owner@example.com"));
        Assert.Null(InstallRoleResolver.Propose("  ", "owner@example.com"));
    }

    [Fact]
    public void Propose_ReturnsNull_WhenNoHouseholdSnapshotHasEverBeenRead()
    {
        Assert.Null(InstallRoleResolver.Propose("owner@example.com", null));
        Assert.Null(InstallRoleResolver.Propose("owner@example.com", string.Empty));
    }

    [Fact]
    public void Propose_ReturnsOwner_WhenTheSignedInAccountPublishedTheSnapshot()
    {
        Assert.Equal(
            InstallRole.Owner,
            InstallRoleResolver.Propose("owner@example.com", "owner@example.com"));
    }

    [Fact]
    public void Propose_IsCaseInsensitive_AndTrimsWhitespace()
    {
        Assert.Equal(
            InstallRole.Owner,
            InstallRoleResolver.Propose("  Owner@Example.com ", "owner@example.com"));
    }

    [Fact]
    public void Propose_ReturnsHers_WhenTheSignedInAccountDiffersFromThePublisher()
    {
        Assert.Equal(
            InstallRole.Hers,
            InstallRoleResolver.Propose("wife@example.com", "owner@example.com"));
    }

    [Fact]
    public void Resolve_DefaultsToOwner_WhenNothingIsKnown()
    {
        // The safe fallback (OwnerColumn's own shape): every install before this release existed
        // behaved as the owner's, and a fresh install — hers included — must not flip to read-only
        // before it has ever read a household snapshot.
        Assert.Equal(InstallRole.Owner, InstallRoleResolver.Resolve(null, null, null));
        Assert.Equal(InstallRole.Owner, InstallRoleResolver.Resolve(null, "wife@example.com", null));
    }

    [Fact]
    public void Resolve_FollowsTheProposal_WhenBothAccountsAreKnown()
    {
        Assert.Equal(
            InstallRole.Hers,
            InstallRoleResolver.Resolve(null, "wife@example.com", "owner@example.com"));
        Assert.Equal(
            InstallRole.Owner,
            InstallRoleResolver.Resolve(null, "owner@example.com", "owner@example.com"));
    }

    [Fact]
    public void Resolve_AnExplicitChoice_AlwaysWinsOverTheProposal()
    {
        // D44 rules out a UI picker, so nothing sets this today — but the contract holds the shape.
        Assert.Equal(
            InstallRole.Owner,
            InstallRoleResolver.Resolve(InstallRole.Owner, "wife@example.com", "owner@example.com"));
        Assert.Equal(
            InstallRole.Hers,
            InstallRoleResolver.Resolve(InstallRole.Hers, "owner@example.com", "owner@example.com"));
    }
}
