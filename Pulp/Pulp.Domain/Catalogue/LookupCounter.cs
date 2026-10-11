using System.Collections.Concurrent;

namespace Pulp.Domain.Catalogue;

/// <summary>The outside sources Pulp looks titles up in.</summary>
public enum LookupSource
{
    StreamingAvailability,
    TvMaze,
    Omdb,
    YouTube,

    /// <summary>MLB's own Stats API (D208).</summary>
    MlbStats,

    /// <summary>ESPN's public JSON (D208).</summary>
    Espn,
}

/// <summary>
/// Counts each request sent to a source, for Diagnostics (D261: the allowance is recorded there and
/// never raised as a notice). The clients count; nothing else reads the count back into the app.
/// </summary>
public interface ILookupCounter
{
    void Counted(LookupSource source);
}

/// <summary>An in-memory tally of lookups by source, safe to count into from several requests at once.</summary>
public sealed class LookupTally : ILookupCounter
{
    private readonly ConcurrentDictionary<LookupSource, int> counts = new();

    public void Counted(LookupSource source) => counts.AddOrUpdate(source, 1, (key, count) => count + 1);

    public int Count(LookupSource source) => counts.GetValueOrDefault(source);

    public int Total => counts.Values.Sum();
}
