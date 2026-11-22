using System.Text;

namespace Codex.Domain.Diagnostics;

/// <summary>One link of a thrown Java exception's cause chain: its Java class, its message and its frames as class.method.</summary>
public sealed record JavaThrowableLink(string ClassName, string? Message, IReadOnlyList<string> Frames);

/// <summary>
/// A readable one-liner for a thrown Java exception (Flyleaf's policy pass). A bare RuntimeException from the
/// platform carries no message, so the line names the Java class (not the .NET wrapper's type) and the top
/// <see cref="FramesPerLink"/> frames it was thrown from, then each cause the same way, up to
/// <see cref="MaxCauses"/> deep, since the real reason is often on a cause.
/// </summary>
public static class JavaThrowableText
{
    public const int FramesPerLink = 3;
    public const int MaxCauses = 2;

    /// <summary><paramref name="chain"/> is the thrown exception first, then its causes in order.</summary>
    public static string Describe(IReadOnlyList<JavaThrowableLink> chain)
    {
        if (chain.Count == 0)
        {
            return "(no exception)";
        }
        var text = new StringBuilder();
        for (var i = 0; i < chain.Count && i <= MaxCauses; i++)
        {
            if (i > 0)
            {
                text.Append(" <- ");
            }
            var link = chain[i];
            text.Append(link.ClassName).Append(": ")
                .Append(string.IsNullOrEmpty(link.Message) ? "(no message)" : link.Message);
            var frames = link.Frames.Take(FramesPerLink).ToList();
            if (frames.Count > 0)
            {
                text.Append(" at ").Append(string.Join(", ", frames));
            }
        }
        return text.ToString();
    }
}
