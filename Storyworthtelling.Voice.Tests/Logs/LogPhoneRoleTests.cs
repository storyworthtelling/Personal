using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Logs;
using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Tests.Logs;

// Which phone a batch came from (plan §3.2), by the rule the apps already use for the shared vault: the
// account that owns the vault file is the owner's; one it was shared with is the partner's. Only the owner's
// phone may create the log folder (§5.6) — her phone writes into the folder shared with her, or not at all.
public class LogPhoneRoleTests
{
    [Theory]
    [InlineData(SharedVaultAccess.ReadWrite, LogPhone.Owner, true)]
    [InlineData(SharedVaultAccess.Claimable, LogPhone.Owner, true)]
    [InlineData(SharedVaultAccess.ReadOnly, LogPhone.Partner, false)]
    [InlineData(SharedVaultAccess.None, LogPhone.Unknown, false)]
    public void TheVaultsOwnershipNamesThePhone(SharedVaultAccess access, string phone, bool mayCreate)
    {
        Assert.Equal(phone, LogPhoneRole.From(access));
        Assert.Equal(mayCreate, LogPhoneRole.MayCreateFolder(LogPhoneRole.From(access)));
    }
}
