using Foreword.Domain.Delivery;
using Storyworthtelling.Remarkable;
using Storyworthtelling.Voice.Health;

namespace Foreword.Infrastructure.Delivery;

/// <summary>One document of the morning and how to send it; a null send means it is not due today.</summary>
public sealed record MorningStep(MorningDocument Document, Func<Task<string?>>? Send);

/// <summary>
/// The morning as one run, for the delivery worker and the Deliver now button alike (audit 11 §6–7). Each document is
/// sent in its own try, so one failing cannot take the others down; each that lands is marked in the day's
/// <see cref="MorningLedger"/> straight away, so a retry — or a second press — sends only what is still missing and the
/// tablet never gets a second brief. The outcome names each document that did not arrive, with a reason a person can
/// act on: no network, the tablet's own refusal, or what the failure said.
/// </summary>
public sealed class MorningRun(Func<string?> readLedger, Action<string> writeLedger)
{
    public async Task<MorningOutcome> Deliver(DateOnly today, IReadOnlyList<MorningStep> steps)
    {
        var outcome = new MorningOutcome();
        foreach (var step in steps)
        {
            if (step.Send is null)
            {
                outcome.Skipped(step.Document);
                continue;
            }

            if (MorningLedger.Read(readLedger(), today).Delivered(step.Document))
            {
                outcome.AlreadyDelivered(step.Document);
                continue;
            }

            try
            {
                var detail = await step.Send();
                writeLedger(MorningLedger.Read(readLedger(), today).With(step.Document).Write());
                outcome.Delivered(step.Document, detail);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                outcome.Failed(step.Document, Reason(exception));
            }
        }
        return outcome;
    }

    /// <summary>Why a document did not arrive, in words the owner can act on.</summary>
    public static string Reason(Exception exception) => exception switch
    {
        _ when NetworkFailure.IsOffline(exception) => "no network",
        RemarkableRefusedException refused => refused.Sentence,
        _ => exception.Message,
    };
}
