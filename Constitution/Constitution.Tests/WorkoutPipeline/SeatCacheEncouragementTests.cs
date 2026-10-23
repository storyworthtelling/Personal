using Constitution.Domain.Voice;
using Constitution.Infrastructure.Voice;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Voice;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>The encouragement seat for a lift that has gone up: the voiced line when there is one, the plain sentence when there is not, nothing when there is no news.</summary>
public class SeatCacheEncouragementTests
{
    private static readonly DateTimeOffset Now = Est(11, 3, 6);
    private static readonly ProgressionFacts Up = new("Squat holding a weight at your chest", 15, 10);

    private static (SeatCacheEncouragement Seat, CountingComposer Composer) Make(bool voiced = true)
    {
        var composer = new CountingComposer { Voiced = voiced };
        return (new SeatCacheEncouragement(new SeatCache(composer, new InMemorySeatLineStore())), composer);
    }

    [Fact]
    public async Task ALiftThatWentUpIsComposed()
    {
        var (seat, composer) = Make();

        var line = await seat.LineAsync(Up, Now, CancellationToken.None);

        Assert.Equal("voiced line 1", line!.Text);
        Assert.True(line.IsVoiced);
        Assert.Equal(1, composer.Calls);
    }

    [Fact]
    public async Task WithNoVoiceThePlainSentenceIsTheLine()
    {
        var (seat, _) = Make(voiced: false);

        var line = await seat.LineAsync(Up, Now, CancellationToken.None);

        Assert.Equal("15 lb today, up from 10.", line!.Text);
        Assert.False(line.IsVoiced);
    }

    [Fact]
    public async Task ALoadThatIsNotUpIsNoNewsAndNobodyIsAsked()
    {
        var (seat, composer) = Make();

        var line = await seat.LineAsync(new ProgressionFacts("Wall angels", 10, 10), Now, CancellationToken.None);

        Assert.Null(line);
        Assert.Equal(0, composer.Calls);
    }

    [Fact]
    public async Task TheSameFactsTheSameDayAreComposedOnce()
    {
        var (seat, composer) = Make();

        await seat.LineAsync(Up, Now, CancellationToken.None);
        await seat.LineAsync(Up, Now.AddHours(3), CancellationToken.None);

        Assert.Equal(1, composer.Calls);
    }
}
