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
    /// <param name="detail">One enum word saying more about the outcome (a sealed write's outcome, say), printed in
    /// brackets. Anything that is not a single word is left out, so a message can never ride in here.</param>
    public static (bool Problem, string Line) Restored(string vault, string status, Exception? failure = null, string? detail = null) =>
        (status is not ("Loaded" or "NoPassphrase"), Line(vault, "restore", status, failure, detail));

    public static (bool Problem, string Line) Published(string vault, string status, Exception? failure = null, string? detail = null) =>
        (!QuietPublishes.Contains(status), Line(vault, "publish", status, failure, detail));

    private static string Line(string vault, string action, string status, Exception? failure, string? detail)
    {
        var line = $"VAULT {vault} {action}: {status}";
        if (IsWord(detail))
        {
            line += $" [{detail}]";
        }

        return failure is null ? line : $"{line} ({ExceptionShape.TypeChain(failure)})";
    }

    private static bool IsWord(string? detail) =>
        !string.IsNullOrEmpty(detail) && detail.Length <= 40 && detail.All(char.IsAsciiLetterOrDigit);
}
