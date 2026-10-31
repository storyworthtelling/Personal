using System.Collections.Concurrent;
using Constitution.Domain.Voice;

namespace Constitution.Infrastructure.Voice;

/// <summary>
/// Composes each seat at most once per (seat, facts, day) — Index's seat rule — so the voice costs
/// pennies and never flickers between two phrasings of the same facts. Every page asks on every
/// appearance and every re-render; the line only has something new to say when the facts change or
/// the day turns.
///
/// <list type="bullet">
/// <item>A <b>voiced</b> line is saved and reused for the rest of that local day. Yesterday's lines are forgotten the first time today's is saved.</item>
/// <item>A <b>plain fallback is never saved</b> as the day's line: the voice may be missing only because the key had not arrived yet. It is remembered as a failure for <see cref="FailureMemoryMinutes"/> minutes (in memory only), so a tab switch straight afterwards is not a retry storm, and then asked again.</item>
/// <item>Two asks for the same line at once compose it once; asks for different lines do not wait for each other.</item>
/// <item>Cancellation is the caller leaving: it propagates and leaves nothing saved and no failure remembered.</item>
/// </list>
///
/// <para><b>Time is an argument.</b> The day is the date on the wall clock of the <c>now</c> passed in
/// (its own offset, not UTC and not the machine's zone), so the day turns at the owner's midnight and a
/// test can say what time it is.</para>
///
/// <para>This sits over <see cref="ISeatComposer"/> and builds no model request itself, nor takes or
/// carries an error; the log is not touched, because a cache hit has nothing to say about the facts.</para>
/// </summary>
public sealed class SeatCache(ISeatComposer inner, ISeatLineStore store)
{
    public const int FailureMemoryMinutes = 10;

    private readonly ConcurrentDictionary<SeatLineKey, SemaphoreSlim> gates = new();
    private readonly ConcurrentDictionary<SeatLineKey, DateTimeOffset> retryAfter = new();

    public async Task<SeatLine> ComposeAsync(SeatRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var day = DateOnly.FromDateTime(now.DateTime);
        var key = SeatLineKey.For(request, day);

        var gate = gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var stored = store.Find(key);
            if (stored is not null && !string.IsNullOrWhiteSpace(stored.Text))
            {
                return stored;
            }

            if (retryAfter.TryGetValue(key, out var notBefore) && now < notBefore)
            {
                return new SeatLine(request.Seat, request.Fallback, IsVoiced: false);
            }

            var line = await inner.ComposeAsync(request, cancellationToken);
            if (line.IsVoiced && !string.IsNullOrWhiteSpace(line.Text))
            {
                store.Save(key, line);
                store.DropBefore(day);
                retryAfter.TryRemove(key, out _);
                Forget(day);
            }
            else
            {
                retryAfter[key] = now.AddMinutes(FailureMemoryMinutes);
            }

            return line;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Drops the in-memory bookkeeping of earlier days; the store drops its own.</summary>
    private void Forget(DateOnly day)
    {
        foreach (var stale in retryAfter.Keys.Where(key => key.Day < day))
        {
            retryAfter.TryRemove(stale, out _);
        }

        foreach (var stale in gates.Keys.Where(key => key.Day < day))
        {
            gates.TryRemove(stale, out _);
        }
    }
}
