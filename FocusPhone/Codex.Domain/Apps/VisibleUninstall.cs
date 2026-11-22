namespace Codex.Domain.Apps;

/// <summary>How the system uninstaller's answer settles an uninstall: whether it worked, and a short reason word.</summary>
public readonly record struct UninstallVerdict(bool Succeeded, string? Reason, bool ShowsProblem);

/// <summary>
/// The system uninstaller, started for a result (<c>EXTRA_RETURN_RESULT</c>), answers with an activity result code:
/// OK when the app was removed, CANCELED when the owner backed out, anything else when it refused. The reason is a
/// fixed word, never the platform's message. Backing out is not a problem worth a notice; a refusal is.
/// </summary>
public static class VisibleUninstall
{
    /// <summary>Android's <c>Activity.RESULT_OK</c>.</summary>
    public const int ResultOk = -1;

    /// <summary>Android's <c>Activity.RESULT_CANCELED</c>.</summary>
    public const int ResultCanceled = 0;

    /// <summary>No uninstaller could be started at all.</summary>
    public static readonly UninstallVerdict Unavailable = new(false, "no uninstaller", true);

    public static UninstallVerdict Read(int resultCode) => resultCode switch
    {
        ResultOk => new UninstallVerdict(true, null, false),
        ResultCanceled => new UninstallVerdict(false, "canceled", false),
        _ => new UninstallVerdict(false, "refused", true),
    };
}
