using Constitution.Domain.Body;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.Infrastructure.Body;

/// <summary>
/// The morning's recovery read, from this phone's Body store through <see cref="RecoveryReader"/>. The store holds one
/// body, the owner's: anyone else asked about (the other adult, whose body lives on her phone) is absent, and an absent
/// person is left exactly as planned. An empty store is absent too, not a read of nothing. Nothing leaves the phone and
/// nothing is logged here (D98): the answer goes to the planner in memory and no value is written anywhere.
/// </summary>
public sealed class BodyRecoveryReadSource : IRecoveryReadSource
{
    private readonly string owner;
    private readonly IBodyStore store;

    public BodyRecoveryReadSource(string owner, IBodyStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(store);
        this.owner = owner.Trim();
        this.store = store;
    }

    public IReadOnlyDictionary<string, RecoveryRead> Read(IReadOnlyList<string> people, DateOnly morning)
    {
        ArgumentNullException.ThrowIfNull(people);
        var found = new Dictionary<string, RecoveryRead>(StringComparer.OrdinalIgnoreCase);
        var asked = people.FirstOrDefault(person => string.Equals(person?.Trim(), owner, StringComparison.OrdinalIgnoreCase));
        if (asked is null || store.IsEmpty)
        {
            return found;
        }

        found[asked] = RecoveryReader.Read(store.All(), morning);
        return found;
    }
}
