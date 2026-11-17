using System.Collections.Concurrent;
using Constitution.Domain.Voice;
using Storyworthtelling.CallSheet;

namespace Constitution.Infrastructure.Voice;

/// <summary>
/// Composes each seat by the suite's one rule (<see cref="SeatPlan"/>, audit W10.3): at most once per seat, facts and
/// day (the day turning at five in the morning on the wall clock passed in), kept across a restart in the seat
/// memory file, and an attempt that came back empty believed for <see cref="SeatPlan.RetryAfter"/> and then asked
/// again — so the voice costs pennies, never flickers between two phrasings of the same facts, and comes back soon
/// after a key arrives.
///
/// <list type="bullet">
/// <item>Each set of facts keeps its own line (the facts' fingerprint is the memory's subject), so every signal's
/// page has its own sentence.</item>
/// <item>Two asks for the same line at once compose it once; asks for different lines do not wait for each other.</item>
/// <item>Cancellation is the caller leaving: it propagates and leaves nothing remembered.</item>
/// </list>
///
/// <para>This sits over <see cref="ISeatComposer"/> and builds no model request itself, nor takes or carries an
/// error; the log is not touched, because a remembered line has nothing to say about the facts.</para>
/// </summary>
public sealed class SeatCache(ISeatComposer inner, SeatMemoryFile memory)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();

    public async Task<SeatLine?> ComposeAsync(SeatRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var line = VoicedOccasions.For(request.Seat).Line;
        var hash = Hash(request);
        var today = SeatPlan.Today(now.DateTime);
        var gate = gates.GetOrAdd($"{line.Seat}/{hash}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var remembered = memory.Get(line, hash);
            switch (SeatPlan.Decide(line, voiceReady: true, hash, hasFacts: true, remembered, now, today))
            {
                case SeatAction.Reuse:
                    return new SeatLine(request.Seat, remembered!.Line!);
                case SeatAction.Fallback:
                    return null;
            }

            var composed = await inner.ComposeAsync(request, cancellationToken);
            memory.Put(line, hash, SeatPlan.Remember(hash, today, composed?.Text, now));
            return composed;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// A seat's facts as the memory knows them: SHA-256 of the app and seat and the ordered facts, cut to 16 hex
    /// characters (<see cref="SeatPlan.Hash"/>). Never <c>string.GetHashCode</c>, which is randomised per process.
    /// </summary>
    public static string Hash(SeatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SeatPlan.Hash(VoicedOccasions.For(request.Seat).Line, request.Facts);
    }
}
