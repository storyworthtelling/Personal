namespace Folio.Domain.Pipeline;

// Tells "the phone ran out of memory" from any other failure. It is not worth a retry: the same file
// in the same heap runs out again, and each attempt that gets partway first fills the heap further.
// Android reports it as a Java error whose managed wrapper carries only its message, so the message is
// as much of the signal as the type.
public static class MemoryFailure
{
    private static readonly string[] Phrases =
    [
        "OutOfMemory", "Failed to allocate", "Java heap space", "until OOM"
    ];

    public static bool IsOutOfMemory(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OutOfMemoryException or InsufficientMemoryException)
                return true;
            if (Phrases.Any(phrase => current.Message.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
