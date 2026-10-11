using Pulp.Domain.Core;

namespace Pulp.Domain.Hidden;

/// <summary>
/// Remembers what was last sent to Flyleaf in this process so the hidden set goes out at launch and
/// then only when the ticked set is a different set (G17). The comparison is of sets, so order and
/// repeats are no difference, and the empty set counts as a set: it is how unticking un-hides.
/// A send that did not go out can be <see cref="Forget"/>-ten so the next call tries again.
/// </summary>
public sealed class HiddenAppsSendGate
{
    private readonly object gate = new();
    private string? lastSent;

    public bool ShouldSend(IEnumerable<ServiceId> ticked)
    {
        ArgumentNullException.ThrowIfNull(ticked);
        var key = string.Join('\n', ticked.Select(service => service.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        lock (gate)
        {
            if (string.Equals(lastSent, key, StringComparison.Ordinal))
            {
                return false;
            }

            lastSent = key;
            return true;
        }
    }

    public void Forget()
    {
        lock (gate)
        {
            lastSent = null;
        }
    }
}
