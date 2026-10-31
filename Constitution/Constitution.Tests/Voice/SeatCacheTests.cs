using Constitution.Domain.Voice;
using Constitution.Infrastructure.Voice;

namespace Constitution.Tests.Voice;

/// <summary>
/// C-9.2.3 — a seat is composed at most once per facts and day (Index's seat rule, mirrored): the
/// pages ask on every appearance and every re-render, and the line only has something new to say when
/// the facts change or the day turns. Everything is faked; time is passed in.
///
/// <para>The bugs these reintroduce: a model call on every tab switch (the voice stops costing
/// pennies); a line that flickers between two phrasings of the same facts; and the quieter opposite —
/// a plain fallback remembered all day, so the voice never comes back after a key arrives.</para>
/// </summary>
public class SeatCacheTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 17, 7, 0, 0, TimeSpan.FromHours(-5));

    private readonly CountingComposer inner = new();
    private readonly InMemorySeatLineStore store = new();

    private SeatCache Cache() => new(inner, store);

    private static SeatRequest Today(params string[] facts) =>
        new(VoiceSeat.TodayTopLine, facts.Length == 0 ? ["strength at 5:30", "lentil soup tonight"] : facts, "Strength at 5:30. Lentil soup tonight.");

    // ---- reuse ----

    [Fact]
    public async Task SameFactsSameDayReuse()
    {
        var cache = Cache();

        var first = await cache.ComposeAsync(Today(), Morning, default);
        var second = await cache.ComposeAsync(Today(), Morning.AddHours(9), default);

        Assert.Equal(1, inner.Calls);
        Assert.True(first.IsVoiced);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task TheStoredLineSurvivesANewCache()
    {
        // The store is the memory: a fresh cache over the same store (a process restart) does not ask again.
        await Cache().ComposeAsync(Today(), Morning, default);
        var again = await Cache().ComposeAsync(Today(), Morning.AddMinutes(5), default);

        Assert.Equal(1, inner.Calls);
        Assert.True(again.IsVoiced);
    }

    [Fact]
    public async Task ChangedFactsComposeAgain()
    {
        var cache = Cache();

        await cache.ComposeAsync(Today("strength at 5:30", "lentil soup tonight"), Morning, default);
        var changed = await cache.ComposeAsync(Today("strength at 6:00", "lentil soup tonight"), Morning, default);

        Assert.Equal(2, inner.Calls);
        Assert.Equal("voiced line 2", changed.Text);
    }

    [Fact]
    public async Task FactsInADifferentOrderAreDifferentFacts()
    {
        var cache = Cache();

        await cache.ComposeAsync(Today("strength at 5:30", "lentil soup tonight"), Morning, default);
        await cache.ComposeAsync(Today("lentil soup tonight", "strength at 5:30"), Morning, default);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task WhitespaceAloneIsNotAChangeInTheFacts()
    {
        var cache = Cache();

        await cache.ComposeAsync(Today("strength  at 5:30", "lentil soup tonight"), Morning, default);
        await cache.ComposeAsync(Today(" strength at 5:30 ", "lentil soup\ntonight"), Morning, default);

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task ChangingTheFactsAndChangingBackReusesTheFirstLine()
    {
        var cache = Cache();

        var original = await cache.ComposeAsync(Today("strength at 5:30", "lentil soup tonight"), Morning, default);
        await cache.ComposeAsync(Today("strength at 6:00", "lentil soup tonight"), Morning, default);
        var back = await cache.ComposeAsync(Today("strength at 5:30", "lentil soup tonight"), Morning, default);

        Assert.Equal(2, inner.Calls);
        Assert.Equal(original, back);
    }

    [Fact]
    public async Task TheSameFactsOnADifferentSeatAreAnotherLine()
    {
        var cache = Cache();
        await cache.ComposeAsync(new SeatRequest(VoiceSeat.TodayTopLine, ["a quiet day"], "A quiet day."), Morning, default);
        await cache.ComposeAsync(new SeatRequest(VoiceSeat.RestDayLine, ["a quiet day"], "A quiet day."), Morning, default);

        Assert.Equal(2, inner.Calls);
    }

    // ---- the day turns ----

    [Fact]
    public async Task ANextDayComposesAgainEvenWithTheSameFacts()
    {
        var cache = Cache();
        var lateTonight = new DateTimeOffset(2026, 10, 17, 23, 59, 0, TimeSpan.FromHours(-5));

        await cache.ComposeAsync(Today(), lateTonight, default);
        var tomorrow = await cache.ComposeAsync(Today(), lateTonight.AddMinutes(2), default);

        Assert.Equal(2, inner.Calls);
        Assert.Equal("voiced line 2", tomorrow.Text);
    }

    [Fact]
    public async Task TheDayIsTheCallersOwnLocalDateNotUtc()
    {
        // 07:00 and 23:30 on the 17th at -5 are on one local day, though 23:30 at -5 is already the 18th in UTC.
        // The date is the wall clock that was passed in, so the day turns at the owner's midnight.
        var cache = Cache();

        await cache.ComposeAsync(Today(), new DateTimeOffset(2026, 10, 17, 7, 0, 0, TimeSpan.FromHours(-5)), default);
        await cache.ComposeAsync(Today(), new DateTimeOffset(2026, 10, 17, 23, 30, 0, TimeSpan.FromHours(-5)), default);

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task YesterdaysLinesAreForgotten()
    {
        var cache = Cache();
        await cache.ComposeAsync(Today(), Morning, default);
        await cache.ComposeAsync(Today("strength at 6:00", "lentil soup tonight"), Morning, default);
        Assert.Equal(2, store.Count);

        await cache.ComposeAsync(Today(), Morning.AddDays(1), default);

        Assert.Equal(1, store.Count);
    }

    // ---- what is not remembered ----

    [Fact]
    public async Task AFallbackIsNeverStoredAsTheDaysLine()
    {
        inner.Voiced = false;

        var line = await Cache().ComposeAsync(Today(), Morning, default);

        Assert.False(line.IsVoiced);
        Assert.Equal("Strength at 5:30. Lentil soup tonight.", line.Text);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task AFailureIsNotRetriedStraightAwayButIsTriedAgainSoon()
    {
        var cache = Cache();
        inner.Voiced = false;
        await cache.ComposeAsync(Today(), Morning, default);

        // A tab switch a minute later is not a retry storm...
        await cache.ComposeAsync(Today(), Morning.AddMinutes(1), default);
        Assert.Equal(1, inner.Calls);

        // ...but the voice must not stay away all day because one call failed.
        inner.Voiced = true;
        var later = await cache.ComposeAsync(Today(), Morning.AddMinutes(SeatCache.FailureMemoryMinutes), default);

        Assert.Equal(2, inner.Calls);
        Assert.True(later.IsVoiced);
    }

    [Fact]
    public async Task WhileAFailureIsRememberedTheCallersOwnFallbackShows()
    {
        var cache = Cache();
        inner.Voiced = false;
        await cache.ComposeAsync(Today(), Morning, default);

        var again = await cache.ComposeAsync(Today(), Morning.AddMinutes(1), default);

        Assert.False(again.IsVoiced);
        Assert.Equal("Strength at 5:30. Lentil soup tonight.", again.Text);
        Assert.Equal(VoiceSeat.TodayTopLine, again.Seat);
    }

    [Fact]
    public async Task ACallerWhoLeavesStoresNothingAndRemembersNoFailure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cache = Cache();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.ComposeAsync(Today(), Morning, cancelled.Token));

        Assert.Equal(0, store.Count);
        // Leaving is not a failure: the very next ask composes at once rather than waiting out a failure's memory.
        var next = await cache.ComposeAsync(Today(), Morning, default);
        Assert.True(next.IsVoiced);
        Assert.Equal(1, inner.Calls);
    }

    // ---- the keys ----

    [Fact]
    public void TheKeyIsTheStoredSeatIdThenAHashOfTheOrderedFactsThenTheDay()
    {
        var key = SeatLineKey.For(Today(), new DateOnly(2026, 10, 17));

        Assert.Equal("today-top-line", key.SeatId);
        Assert.Equal(new DateOnly(2026, 10, 17), key.Day);
        Assert.Matches("^[0-9a-f]{16}$", key.FactsHash);
        Assert.Equal($"today-top-line|{key.FactsHash}|2026-10-17", key.ToString());
    }

    [Fact]
    public void TheHashIsAFixedFingerprintNotStringGetHashCode()
    {
        // string.GetHashCode is randomised per process and would orphan every stored line on each launch,
        // so the expectation is a literal: SHA-256 of the seat id, a newline and the facts joined by newlines.
        var key = SeatLineKey.For(new SeatRequest(VoiceSeat.RestDayLine, ["a rest day"], "A rest day."), new DateOnly(2026, 10, 17));

        Assert.Equal("6582dd1d6aeb3381", key.FactsHash);
    }

    [Fact]
    public void TheFallbackIsNotPartOfTheKeyButEveryFactIs()
    {
        var day = new DateOnly(2026, 10, 17);
        var key = SeatLineKey.For(new SeatRequest(VoiceSeat.RestDayLine, ["a rest day"], "A rest day."), day);

        Assert.Equal(key.FactsHash,
            SeatLineKey.For(new SeatRequest(VoiceSeat.RestDayLine, ["a rest day"], "Another fallback."), day).FactsHash);
        Assert.NotEqual(key.FactsHash,
            SeatLineKey.For(new SeatRequest(VoiceSeat.RestDayLine, ["a rest day", "dinner is out"], "A rest day."), day).FactsHash);
    }

    [Fact]
    public void EveryKeyUsesTheClosedListsStoredId()
    {
        foreach (var occasion in VoicedOccasions.All)
        {
            Assert.Equal(occasion.Id, SeatLineKey.For(new SeatRequest(occasion.Seat, ["a fact"], "A plain sentence."), new DateOnly(2026, 10, 17)).SeatId);
        }
    }

    // ---- two pages ask at once ----

    [Fact]
    public async Task TwoAsksAtOnceComposeOnce()
    {
        var gate = new TaskCompletionSource();
        inner.Hold = gate.Task;
        var cache = Cache();

        var first = cache.ComposeAsync(Today(), Morning, default);
        var second = cache.ComposeAsync(Today(), Morning, default);
        gate.SetResult();
        var lines = await Task.WhenAll(first, second);

        Assert.Equal(1, inner.Calls);
        Assert.Equal(lines[0], lines[1]);
    }
}

/// <summary>Stands in for <c>SeatComposer</c>: counts the calls and answers voiced or not, as told.</summary>
internal sealed class CountingComposer : ISeatComposer
{
    public int Calls { get; private set; }

    public bool Voiced { get; set; } = true;

    public Task? Hold { get; set; }

    public async Task<SeatLine> ComposeAsync(SeatRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        var call = Calls;
        if (Hold is not null)
        {
            await Hold;
        }

        return Voiced
            ? new SeatLine(request.Seat, $"voiced line {call}", IsVoiced: true)
            : new SeatLine(request.Seat, request.Fallback, IsVoiced: false);
    }
}

/// <summary>The in-memory stand-in for the on-device store (preferences, a later Android concern).</summary>
internal sealed class InMemorySeatLineStore : ISeatLineStore
{
    private readonly Dictionary<SeatLineKey, SeatLine> lines = [];

    public int Count => lines.Count;

    public SeatLine? Find(SeatLineKey key) => lines.GetValueOrDefault(key);

    public void Save(SeatLineKey key, SeatLine line) => lines[key] = line;

    public void DropBefore(DateOnly day)
    {
        foreach (var key in lines.Keys.Where(key => key.Day < day).ToList())
        {
            lines.Remove(key);
        }
    }
}
