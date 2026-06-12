namespace FocusPhone.Domain.Unlock;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
