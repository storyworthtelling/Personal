using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Infrastructure.Documents;

/// <summary>The once-per-week, per-adult record that the digest went: the Saturday it was for. A restart or a second pass must not send it twice.</summary>
public interface IDigestStamps
{
    bool Has(string person, DateOnly saturday);

    void Mark(string person, DateOnly saturday);
}

public abstract record DigestOutcome
{
    private DigestOutcome()
    {
    }

    /// <summary>It is not Saturday morning (or the Sunday after): nothing was read.</summary>
    public sealed record NotDue : DigestOutcome;

    /// <summary>This adult's digest for that Saturday already went.</summary>
    public sealed record AlreadyDelivered(DateOnly Saturday) : DigestOutcome;

    /// <summary>A live store could not answer, so nothing was built or sent. The next pass tries again.</summary>
    public sealed record NotReady(string Reason) : DigestOutcome;

    /// <summary>It reached the tablet, is recorded for the read-back to trash three days on, and is stamped.</summary>
    public sealed record Delivered(string Name) : DigestOutcome;

    /// <summary>The upload failed; the delivery service has raised the reMarkable notice. Not stamped, so the next pass sends it.</summary>
    public sealed record NotDelivered(string Name, Exception Failure) : DigestOutcome;
}

/// <summary>
/// Assembles the Saturday digest from the live stores, draws it to a vector PDF through the shared document renderer, delivers it through the reMarkable
/// seam and records it so the read-back trashes it three days later (<see cref="ReadBackPolicy.DigestDays"/>, R4). Idempotent per adult per Saturday by a stamp
/// written only after the tablet took it. Owns no schedule and no gate: <see cref="DigestSchedule"/> says when, and the head's service takes the shared run gate.
/// The planner notes on its last page are read once on Sunday by the read-back (D170).
/// </summary>
public sealed class SaturdayDigestRun(IDigestSources sources, DeliveryService delivery, IExpectedDocuments expected, IDigestStamps stamps)
{
    public async Task<DigestOutcome> RunAsync(DateTimeOffset now, TimeZoneInfo zone, string person, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(person);
        cancellation.ThrowIfCancellationRequested();

        if (DigestSchedule.DueSaturday(now, zone) is not { } saturday)
        {
            return new DigestOutcome.NotDue();
        }

        if (stamps.Has(person, saturday))
        {
            return new DigestOutcome.AlreadyDelivered(saturday);
        }

        var assembly = DigestAssembler.Assemble(person, saturday, sources);
        if (assembly is DigestAssembly.NotReady notReady)
        {
            return new DigestOutcome.NotReady(notReady.Reason);
        }

        var model = ((DigestAssembly.Ready)assembly).Model;
        var pages = SaturdayDigest.Build(model);
        var pdf = ConstitutionDocumentRenderer.Render(pages);
        var result = await delivery.Deliver(DeliveryKind.Digest, saturday, null, pdf);
        if (!result.Delivered)
        {
            return new DigestOutcome.NotDelivered(result.Name, result.Failure ?? new InvalidOperationException("The tablet did not take the digest."));
        }

        // The last page is read once on Sunday for the planner notes (owner decision amending C-2.2.3); nothing else on the digest is.
        expected.Record(ExpectedDocument.ForDigest(pages[^1], saturday, person));
        stamps.Mark(person, saturday);
        return new DigestOutcome.Delivered(result.Name);
    }
}
