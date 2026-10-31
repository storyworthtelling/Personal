namespace Constitution.Domain.Body;

/// <summary>How one signal's read ended. A shape, never a message: the platform's text can echo what was asked.</summary>
public enum HealthReadOutcome
{
    /// <summary>The platform answered; the samples may be none.</summary>
    Ok,

    /// <summary>This platform has no way to read the signal (a missing record type, a missing service).</summary>
    Unsupported,

    /// <summary>The platform refused: the permission is not held, or was withdrawn mid-read.</summary>
    Denied,

    /// <summary>Anything else went wrong (rate limit, sync in progress, an I/O error).</summary>
    Failed,
}

/// <summary>
/// One platform record reduced to what Body needs: the local day it belongs to, the amount in the signal's
/// canonical unit (see <see cref="BodySignal"/>), and an opaque label for the app that wrote it. The label is
/// only ever compared for equality, so two apps counting the same walk are not added together; it is never
/// stored, shown or logged.
/// </summary>
public sealed record HealthSample(DateOnly Day, double Amount, string Origin);

/// <summary>What a read returned: how it ended, and the samples when it did.</summary>
public sealed record HealthRead(HealthReadOutcome Outcome, IReadOnlyList<HealthSample> Samples)
{
    public static HealthRead Ok(IReadOnlyList<HealthSample> samples) => new(HealthReadOutcome.Ok, samples);
}

/// <summary>
/// Health Connect as the reader sees it. The Android head implements it over
/// <c>Android.Health.Connect.HealthConnectManager</c>; tests use a fake. It never throws for the platform:
/// a refusal, a missing service or a failed call is a <see cref="HealthReadOutcome"/>.
/// </summary>
public interface IHealthRecordSource
{
    /// <summary>True when the platform lets this install read the signal now. The only question about permission the app asks.</summary>
    bool IsGranted(BodySignal signal);

    /// <summary>True when this install may read while the app is closed (R5). Where it is false, values are refreshed when the app opens.</summary>
    bool BackgroundReadsGranted { get; }

    /// <summary>The signal's records for the local days <paramref name="from"/> to <paramref name="to"/> inclusive, in canonical units.</summary>
    Task<HealthRead> ReadAsync(BodySignal signal, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
