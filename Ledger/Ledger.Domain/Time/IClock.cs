namespace Ledger.Domain.Time;

// Abstracts the current time so scheduling/rate-limiting logic stays pure and testable.
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
