using Constitution.Domain.Voice;
using Constitution.Infrastructure.Voice;
using Storyworthtelling.CallSheet;

namespace Constitution.Tests.Voice;

/// <summary>
/// C-9.2.3 — a seat is composed by the suite's one rule (<see cref="SeatPlan"/>, audit W10.3): at most once per
/// facts and day, kept across a restart in the seat memory file, and an attempt that came back empty believed only
/// for <see cref="SeatPlan.RetryAfter"/>. Everything is faked; time is passed in.
///
/// <para>The bugs these reintroduce: a model call on every tab switch; a line that flickers between two phrasings
/// of the same facts; and the quieter opposite — an empty attempt remembered all day, so the voice never comes back
/// after a key arrives.</para>
/// </summary>
public sealed class SeatCacheTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 17, 7, 0, 0, TimeSpan.FromHours(-5));

    private readonly string folder = Directory.CreateTempSubdirectory("constitution-seats-").FullName;
    private readonly CountingComposer inner = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private SeatCache Cache() => new(inner, new SeatMemoryFile(Path.Combine(folder, "voice-seats.json")));

    private static SeatRequest Today(params string[] facts) =>
        new(VoiceSeat.TodayTopLine, facts.Length == 0 ? ["strength at 5:30", "lentil soup tonight"] : facts);

    // ---- reuse ----

    [Fact]
    public async Task SameFactsSameDayReuse()
    {
        var cache = Cache();

        var first = await cache.ComposeAsync(Today(), Morning, default);
        var second = await cache.ComposeAsync(Today(), Morning.AddHours(9), default);

        Assert.Equal(1, inner.Calls);
        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task TheStoredLineSurvivesANewCache()
    {
        // The file is the memory: a fresh cache over the same file (a process restart) does not ask again.
        await Cache().ComposeAsync(Today(), Morning, default);
        var again = await Cache().ComposeAsync(Today(), Morning.AddMinutes(5), default);

        Assert.Equal(1, inner.Calls);
        Assert.Equal("voiced line 1", again!.Text);
    }

    [Fact]
    public async Task ChangedFactsComposeAgain()
    {
        var cache = Cache();

        await cache.ComposeAsync(Today("strength at 5:30", "lentil soup tonight"), Morning, default);
        var changed = await cache.ComposeAsync(Today("strength at 6:00", "lentil soup tonight"), Morning, default);

        Assert.Equal(2, inner.Calls);
        Assert.Equal("voiced line 2", changed!.Text);
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

    /// <summary>Each set of facts keeps its own line: every signal's page has its own sentence.</summary>
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
        await cache.ComposeAsync(new SeatRequest(VoiceSeat.TodayTopLine, ["a quiet day"]), Morning, default);
        await cache.ComposeAsync(new SeatRequest(VoiceSeat.RestDayLine, ["a quiet day"]), Morning, default);

        Assert.Equal(2, inner.Calls);
    }

    // ---- the day turns ----

    [Fact]
    public async Task ANextDayComposesAgainEvenWithTheSameFacts()
    {
        var cache = Cache();

        await cache.ComposeAsync(Today(), Morning, default);
        var tomorrow = await cache.ComposeAsync(Today(), Morning.AddDays(1), default);

        Assert.Equal(2, inner.Calls);
        Assert.Equal("voiced line 2", tomorrow!.Text);
    }

    [Fact]
    public async Task ALateEveningIsStillTheDayItBegan()
    {
        // The suite's one day (SeatPlan.Today): it turns at five in the morning, on the wall clock passed in.
        var cache = Cache();

        await cache.ComposeAsync(Today(), new DateTimeOffset(2026, 10, 17, 22, 0, 0, TimeSpan.FromHours(-5)), default);
        await cache.ComposeAsync(Today(), new DateTimeOffset(2026, 10, 18, 1, 30, 0, TimeSpan.FromHours(-5)), default);

        Assert.Equal(1, inner.Calls);
    }

    // ---- no voice ----

    [Fact]
    public async Task NoVoiceHidesTheSeat()
    {
        inner.Voiced = false;

        Assert.Null(await Cache().ComposeAsync(Today(), Morning, default));
    }

    [Fact]
    public async Task AnEmptyAttemptIsNotRetriedStraightAwayButIsTriedAgainSoon()
    {
        var cache = Cache();
        inner.Voiced = false;
        await cache.ComposeAsync(Today(), Morning, default);

        // A tab switch a minute later is not a retry storm...
        Assert.Null(await cache.ComposeAsync(Today(), Morning.AddMinutes(1), default));
        Assert.Equal(1, inner.Calls);

        // ...but the voice must not stay away all day because one call failed.
        inner.Voiced = true;
        var later = await cache.ComposeAsync(Today(), Morning + SeatPlan.RetryAfter, default);

        Assert.Equal(2, inner.Calls);
        Assert.NotNull(later);
    }

    [Fact]
    public async Task ACallerWhoLeavesStoresNothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cache = Cache();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.ComposeAsync(Today(), Morning, cancelled.Token));

        // Leaving is not an empty attempt: the very next ask composes at once.
        var next = await cache.ComposeAsync(Today(), Morning, default);
        Assert.NotNull(next);
        Assert.Equal(1, inner.Calls);
    }

    // ---- the seat ----

    [Fact]
    public void EverySeatIsItsCallSheetLine()
    {
        foreach (var occasion in VoicedOccasions.All)
        {
            Assert.Equal("Constitution", occasion.Line.App);
            Assert.Equal(occasion.Id, occasion.Line.Seat);
        }
    }

    [Fact]
    public void TheHashIsAFixedFingerprintNotStringGetHashCode()
    {
        // string.GetHashCode is randomised per process and would orphan every stored line on each launch,
        // so the expectation is a literal: SHA-256 of the app and seat, a newline and the facts joined by newlines.
        Assert.Equal("27afbe1ec8a578b0", SeatCache.Hash(new SeatRequest(VoiceSeat.RestDayLine, ["a rest day"])));
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

/// <summary>Stands in for <c>SeatComposer</c>: counts the calls and answers with a line or with nothing, as told.</summary>
internal sealed class CountingComposer : ISeatComposer
{
    public int Calls { get; private set; }

    public bool Voiced { get; set; } = true;

    public Task? Hold { get; set; }

    public async Task<SeatLine?> ComposeAsync(SeatRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        var call = Calls;
        if (Hold is not null)
        {
            await Hold;
        }

        return Voiced ? new SeatLine(request.Seat, $"voiced line {call}") : null;
    }
}
