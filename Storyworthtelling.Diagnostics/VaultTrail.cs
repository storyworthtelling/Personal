namespace Storyworthtelling.Diagnostics;

/// <summary>
/// One line for every vault restore and publish (audit 08 §6–8, §28). They were all swallowed: after a factory reset —
/// the vault's whole reason to exist — a mistyped passphrase or an unreadable vault looked exactly like "nothing
/// restored". The line names the vault, the action and the outcome, and for a failure the exception's type chain:
/// never a value, and never a message, which can quote a token or a path.
/// </summary>
public static class VaultTrail
{
    public const string Shared = "shared";
    public const string Personal = "personal";

    // A publish that this install deliberately does not make is not a failure: no passphrase yet, or the partner's
    // read-only phone.
    private static readonly string[] QuietPublishes = ["Published", "ReadOnly", "NoPassphrase"];

    /// <summary>
    /// A restore is a problem unless the vault opened: an empty vault after a reset is exactly the case to see. No
    /// passphrase yet is the app not taking part, which is said but not flagged.
    /// </summary>
    public static (bool Problem, string Line) Restored(string vault, string status, Exception? failure = null) =>
        (status is not ("Loaded" or "NoPassphrase"), Line(vault, "restore", status, failure));

    public static (bool Problem, string Line) Published(string vault, string status, Exception? failure = null) =>
        (!QuietPublishes.Contains(status), Line(vault, "publish", status, failure));

    private static string Line(string vault, string action, string status, Exception? failure) =>
        failure is null ? $"VAULT {vault} {action}: {status}" : $"VAULT {vault} {action}: {status} ({ExceptionShape.TypeChain(failure)})";
}
