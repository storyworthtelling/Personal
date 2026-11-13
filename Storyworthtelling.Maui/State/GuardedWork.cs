using Storyworthtelling.Diagnostics;
using Storyworthtelling.Folds;

namespace Storyworthtelling.Maui.State;

/// <summary>
/// The body of an <c>async void</c> handler (audit 11 §15). A handler with no try crashes the app on a database or
/// navigation failure, and inside an undo window the undo goes with it; this awaits the work and folds any failure into
/// the dog-ear in plain words (<see cref="PlainError"/>), the pattern Flyleaf and Plate follow by hand. Success
/// withdraws the notice the same key raised before, since success is a state change and not a message.
/// </summary>
public static class GuardedWork
{
    /// <param name="key">The notice's key, so a later success withdraws it.</param>
    /// <param name="headline">What did not happen, e.g. "Could not remove the account".</param>
    /// <param name="log">Where the failure itself goes (the head's file log); the screen gets only the words.</param>
    /// <param name="service">Who a refusal would come from, for the words.</param>
    /// <returns>Whether the work finished.</returns>
    public static async Task<bool> Run(
        DogEarState folds, string key, string headline, Func<Task> work, Action<Exception>? log = null, string? service = null)
    {
        try
        {
            await work();
            folds.SetNotice(key, null);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception failure)
        {
            log?.Invoke(failure);
            var words = PlainError.Describe(failure, service);
            folds.SetNotice(key, new DogEarNotice(headline, Sentence(words), DogEarHue.Problem, At: DateTimeOffset.Now));
            return false;
        }
    }

    private static string Sentence(string words) =>
        words.Length == 0 ? "Something went wrong." : char.ToUpperInvariant(words[0]) + words[1..].TrimEnd('.') + ".";
}
