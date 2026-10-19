using System.Net;
using Constitution.Domain.Diagnostics;

namespace Constitution.Domain.ReadBack;

/// <summary>What one morning's read-back did, document by document.</summary>
public sealed class ReadBackReport(IReadOnlyList<DocumentReadBack> documents)
{
    public IReadOnlyList<DocumentReadBack> Documents { get; } = documents ?? throw new ArgumentNullException(nameof(documents));

    /// <summary>
    /// The one word Diagnostics keeps for the morning. A page with no ink on it (R3) outranks everything, because it is
    /// the one result that means the whole round trip may be broken. Otherwise a morning where nothing went wrong
    /// succeeded; one where everything that went wrong was the tablet's answer is refused or unreachable; and any mix
    /// of done and not done, or a local failure, is partial.
    /// </summary>
    public DiagnosticsOutcome Outcome
    {
        get
        {
            if (Documents.Any(document => document is DocumentReadBack.NoMarksInCloud))
            {
                return DiagnosticsOutcome.FoundNothingRead;
            }

            var notDone = Documents.Where(document => document is DocumentReadBack.Failed or DocumentReadBack.Unfinished).ToList();
            if (notDone.Count == 0)
            {
                return DiagnosticsOutcome.Succeeded;
            }

            if (notDone.Count < Documents.Count)
            {
                return DiagnosticsOutcome.Partial;
            }

            var failures = notDone.OfType<DocumentReadBack.Failed>().Select(failed => failed.Failure).ToList();
            if (failures.Any(IsRefusal))
            {
                return DiagnosticsOutcome.Refused;
            }

            return failures.Any(IsUnreachable) ? DiagnosticsOutcome.Unreachable : DiagnosticsOutcome.Partial;
        }
    }

    private static bool IsRefusal(Exception failure) =>
        failure is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden };

    private static bool IsUnreachable(Exception failure) =>
        failure is HttpRequestException || failure is TaskCanceledException { InnerException: TimeoutException };
}
