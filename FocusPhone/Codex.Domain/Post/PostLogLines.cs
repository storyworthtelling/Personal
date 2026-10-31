using System.Globalization;

namespace Codex.Domain.Post;

/// <summary>
/// Post's lines for the log loop (LOG-LOOP-PLAN.md §4.5, G10): the SMS role on every launch, every SMS and
/// MMS result code, each MMS download's completion, the mail route chosen and a filing delete that keeps
/// failing. Fixed wording, because the reader's digest groups on it. Result codes are named from
/// Mono.Android's <c>Android.Telephony.SmsResultError</c> and <c>Android.Telephony.MmsError</c>.
/// </summary>
public static class PostLogLines
{
    /// <summary><c>Activity.RESULT_OK</c>, which is what a sent-intent carries on success.</summary>
    public const int ResultOk = -1;

    private static readonly Dictionary<int, string> SmsErrors = new()
    {
        [1] = "GENERIC_FAILURE",
        [2] = "RADIO_OFF",
        [3] = "NULL_PDU",
        [4] = "NO_SERVICE",
        [5] = "LIMIT_EXCEEDED",
        [6] = "FDN_CHECK_FAILURE",
        [7] = "SHORT_CODE_NOT_ALLOWED",
        [8] = "SHORT_CODE_NEVER_ALLOWED",
    };

    private static readonly Dictionary<int, string> MmsErrors = new()
    {
        [1] = "UNSPECIFIED",
        [2] = "INVALID_APN",
        [3] = "UNABLE_CONNECT_MMS",
        [4] = "HTTP_FAILURE",
        [5] = "IO_ERROR",
        [6] = "RETRY",
        [7] = "CONFIGURATION_ERROR",
        [8] = "NO_DATA_NETWORK",
        [9] = "INVALID_SUBSCRIPTION_ID",
        [10] = "INACTIVE_SUBSCRIPTION",
        [11] = "DATA_DISABLED",
        [12] = "MMS_DISABLED_BY_CARRIER",
    };

    public static string SmsRole(SmsRoleStatus status, SmsRoleNotice notice)
    {
        var line = $"SMS ROLE {status}";
        if (notice.WasLost)
        {
            line += " — lost since the last launch";
        }
        if (notice.Speaks)
        {
            line += (notice.WasLost ? "; " : " — ") + "told the owner: " + notice.Headline;
        }
        return line;
    }

    public static string SmsSent(int resultCode) =>
        resultCode == ResultOk ? "SMS SENT ok" : "SMS SEND FAILED: " + Named(SmsErrors, resultCode);

    public static string MmsSent(int resultCode) => "MMS SEND " + MmsOutcome(resultCode);

    public static string MmsDownloaded(int resultCode) => "MMS DOWNLOAD " + MmsOutcome(resultCode);

    public static string MailRoute(MailTransport route) => $"MAIL ROUTE {route}";

    /// <summary>
    /// Null until the delete has failed often enough to be worth a line (the fold's own threshold), then on
    /// every tenth try after it — the sync retries every half hour, and a line each time would bury the log.
    /// </summary>
    public static string? OwedDeleteFailing(int failedAttempts) =>
        failedAttempts < Tombstones.QuietAttempts
        || (failedAttempts > Tombstones.QuietAttempts && failedAttempts % 10 != 0)
            ? null
            : string.Create(CultureInfo.InvariantCulture,
                $"FILING the delete at the source has failed {failedAttempts} times; the item stays filed");

    private static string MmsOutcome(int resultCode) =>
        resultCode == ResultOk ? "ok" : "FAILED: " + Named(MmsErrors, resultCode);

    private static string Named(Dictionary<int, string> names, int code) =>
        names.TryGetValue(code, out var name)
            ? string.Create(CultureInfo.InvariantCulture, $"{name} ({code})")
            : string.Create(CultureInfo.InvariantCulture, $"code {code}");
}
