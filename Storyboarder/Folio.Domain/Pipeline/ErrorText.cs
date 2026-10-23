namespace Folio.Domain.Pipeline;

// One line that says what actually went wrong. The outer exception of a dropped connection says only
// that a request failed; the cause ("Software caused connection abort") is several levels down, and a
// diagnostics file that records the outer message alone cannot tell a flaky network from a bad request.
public static class ErrorText
{
    public static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            parts.Add($"{current.GetType().Name}: {current.Message}");
        return string.Join(" ← ", parts);
    }
}
