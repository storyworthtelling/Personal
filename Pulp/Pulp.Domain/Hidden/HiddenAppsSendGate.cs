using Pulp.Domain.Core;

namespace Pulp.Domain.Hidden;

/// <summary>Why the ticked set is being offered to the gate: read off the household, or just changed by a write.</summary>
public enum HiddenSendCause
{
    /// <summary>The household was loaded. An empty set here may only mean a fresh or unsynced install, so it is never sent.</summary>
    Load,

    /// <summary>A tick change was written. An empty set here is the owner unticking the last service, and un-hides.</summary>
    Edit,
}

/// <summary>
/// Remembers what was last sent to Flyleaf in this process so the hidden set goes out at launch and
/// then only when the ticked set is a different set (G17). The comparison is of sets, so order and
/// repeats are no difference, and the empty set counts as a set, but only when an edit made it: it is how unticking un-hides, whereas an empty set read on load is a fresh or unsynced install that must not un-hide what Flyleaf already hides.
/// A send that did not go out can be <see cref="Forget"/>-ten so the next call tries again.
/// </summary>
public sealed class HiddenAppsSendGate
{
    private readonly object gate = new();
    private string? lastSent;

    public bool ShouldSend(IEnumerable<ServiceId> ticked, HiddenSendCause cause)
    {
        ArgumentNullException.ThrowIfNull(ticked);
        var key = string.Join('\n', ticked.Select(service => service.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        if (key.Length == 0 && cause == HiddenSendCause.Load)
        {
            return false;
        }

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
