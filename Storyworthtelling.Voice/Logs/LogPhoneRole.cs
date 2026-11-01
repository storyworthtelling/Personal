using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Logs;

/// <summary>
/// Which phone a batch comes from (LOG-LOOP-PLAN.md §3.2), by the rule every app already uses for the shared
/// vault: the account that owns the vault file is the owner's (and one that may still claim it will be);
/// an account the file was shared with is the partner's. Only the owner's phone creates the log folder.
/// </summary>
public static class LogPhoneRole
{
    public static string From(SharedVaultAccess access) => access switch
    {
        SharedVaultAccess.ReadWrite or SharedVaultAccess.Claimable => LogPhone.Owner,
        SharedVaultAccess.ReadOnly => LogPhone.Partner,
        _ => LogPhone.Unknown,
    };

    public static bool MayCreateFolder(string phone) => phone == LogPhone.Owner;
}
