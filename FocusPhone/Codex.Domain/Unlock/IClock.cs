namespace Codex.Domain.Unlock;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
