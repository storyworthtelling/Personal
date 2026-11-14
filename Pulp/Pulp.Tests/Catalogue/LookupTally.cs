using System.Collections.Concurrent;
using Pulp.Domain.Catalogue;

namespace Pulp.Tests;

/// <summary>An in-memory tally of lookups by source for the client tests, safe to count into from several requests at once.
/// Moved here from Pulp.Domain (orphan audit §31): only tests ever built one.</summary>
public sealed class LookupTally : ILookupCounter
{
    private readonly ConcurrentDictionary<LookupSource, int> counts = new();

    public void Counted(LookupSource source) => counts.AddOrUpdate(source, 1, (key, count) => count + 1);

    public int Count(LookupSource source) => counts.GetValueOrDefault(source);

    public int Total => counts.Values.Sum();
}
