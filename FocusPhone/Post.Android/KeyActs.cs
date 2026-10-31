using Codex.Domain.Diagnostics;

namespace Post;

/// <summary>
/// Running a local act as a declared key action (LOG-LOOP-PLAN.md §4.1): it succeeds when the act returns
/// and fails, with the exception's type and message, when it throws — rethrown, so the act behaves exactly
/// as it did before it was watched.
/// </summary>
internal static class KeyActs
{
    /// <summary>The window for an act that is over when its method returns (file, delete).</summary>
    public static readonly TimeSpan Local = TimeSpan.FromSeconds(10);

    public static void Run(KeyAction action, Action act)
    {
        try
        {
            act();
            action.Succeeded();
        }
        catch (Exception e)
        {
            action.Failed($"{e.GetType().Name}: {e.Message}");
            throw;
        }
    }
}
