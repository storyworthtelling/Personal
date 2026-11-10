namespace Storyworthtelling.Diagnostics;

/// <summary>
/// A failure by its shape: the type chain and the frames, never a message (audit 08 §9). A message can carry what the
/// code was holding — a Body reading, a meal, a balance — and a frame cannot, so a place that may hold values logs this.
/// </summary>
public static class ExceptionShape
{
    /// <summary>The types outermost first, joined with " &lt; ".</summary>
    public static string TypeChain(Exception exception)
    {
        var types = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            types.Add(current.GetType().FullName ?? current.GetType().Name);
        }
        return string.Join(" < ", types);
    }

    /// <summary>The type chain, then each exception's frames, outermost first.</summary>
    public static string Describe(Exception exception)
    {
        var lines = new List<string> { TypeChain(exception) };
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.StackTrace is not { } stack)
            {
                continue;
            }
            lines.AddRange(stack.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Where(line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal))
                .Select(line => "   " + line.Trim()));
        }
        return string.Join('\n', lines);
    }
}
