using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Voice.Claude;

/// <summary>
/// One log line for a failed Claude call (audit 08 §12): the call's name, the failure kind, the HTTP status and the
/// request id, which is what tells a rejected key from a 400 from an empty balance. Never the reply body or the
/// exception's message, which can quote the prompt.
/// </summary>
public static class ClaudeCallLine
{
    public static string Failed(string call, Exception exception)
    {
        if (exception is not ClaudeRequestException refused)
        {
            return $"CLAUDE {call} failed: {ExceptionShape.TypeChain(exception)}";
        }

        var status = refused.StatusCode is { } code ? $" {(int)code}" : string.Empty;
        var request = string.IsNullOrEmpty(refused.RequestId) ? string.Empty : $" (request {refused.RequestId})";
        return $"CLAUDE {call} failed: {refused.Failure}{status}{request}";
    }
}
