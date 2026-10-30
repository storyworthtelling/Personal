using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class VaultOwnershipTests
{
    // The vault file's Drive ownership is the answer: a file this account owns is the admin's
    // install, a file shared with it is someone else's. It is not who published first, and not
    // which account the phone is signed into.
    [Fact]
    public void AFileOwnedByThisAccountMakesTheAdmin()
    {
        Assert.Equal(SharedVaultAccess.ReadWrite, VaultOwnership.Decide(DriveFileOwnership.Owned));
    }

    [Fact]
    public void AFileSharedWithThisAccountIsReadOnly()
    {
        Assert.Equal(SharedVaultAccess.ReadOnly, VaultOwnership.Decide(DriveFileOwnership.Shared));
    }

    // No vault file yet: whoever creates it owns it, so the first install to write becomes the admin
    // by creating the file.
    [Fact]
    public void NoFileYetIsClaimableByTheInstallThatCreatesIt()
    {
        Assert.Equal(SharedVaultAccess.Claimable, VaultOwnership.Decide(DriveFileOwnership.None));
    }

    [Fact]
    public void CanWrite_IsTrueOnlyWhereWritingIsAllowed()
    {
        Assert.True(SharedVaultAccess.ReadWrite.CanWrite());
        Assert.True(SharedVaultAccess.Claimable.CanWrite());
        Assert.False(SharedVaultAccess.ReadOnly.CanWrite());
        Assert.False(SharedVaultAccess.None.CanWrite());
    }
}
