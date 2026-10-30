using System.Runtime.ExceptionServices;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// Several hooks behind the read-back's one. Each is told in the order given and each is told whatever the others did: a failure is held until
/// all have had their turn and then raised as it was (one failure keeps its type and its stack; several arrive together in an
/// <see cref="AggregateException"/>), so the read-back records it at the persist step and keeps the page. Every hook is idempotent, so the next
/// morning's second telling of those that succeeded changes nothing.
/// </summary>
public sealed class SettledHooks : IReadBackSettled, IReadBackObserved
{
    private readonly IReadOnlyList<IReadBackSettled> hooks;

    public SettledHooks(params IReadBackSettled[] hooks)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        if (hooks.Length == 0)
        {
            throw new ArgumentException("A composite of no hooks tells nobody anything; give it at least one.", nameof(hooks));
        }

        foreach (var hook in hooks)
        {
            ArgumentNullException.ThrowIfNull(hook, nameof(hooks));
        }

        this.hooks = hooks.ToList();
    }

    public void Settled(ExpectedDocument document, DocumentResult result) =>
        TellAll(hook => hook.Settled(document, result));

    public void Observed(ExpectedDocument document, DocumentResult result) =>
        TellAll(hook => (hook as IReadBackObserved)?.Observed(document, result));

    private void TellAll(Action<IReadBackSettled> tell)
    {
        var failures = new List<Exception>();
        foreach (var hook in hooks)
        {
            try
            {
                tell(hook);
            }
            catch (Exception failure)
            {
                // Held, not swallowed: it is raised below once the others have had their turn.
                failures.Add(failure);
            }
        }

        switch (failures.Count)
        {
            case 0:
                return;
            case 1:
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
                return;
            default:
                throw new AggregateException("More than one hook could not record what the pages said.", failures);
        }
    }
}
