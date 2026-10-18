using System.Globalization;
using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

public class ChangeWordsTests
{
    private static string Say(BodySignal signal, double current, double previous) =>
        ChangeWords.Phrase(BodyData.Change(signal, current, previous));

    [Fact]
    public void SleepIsSaidInMinutesPerNight()
    {
        Assert.Equal("down about 40 minutes a night", Say(BodySignal.Sleep, 6.0 + (20.0 / 60), 7.0));
        Assert.Equal("up about 30 minutes a night", Say(BodySignal.Sleep, 7.5, 7.0));
    }

    [Fact]
    public void LongSleepGapsAreSaidInHours()
    {
        Assert.Equal("down about 2.5 hours a night", Say(BodySignal.Sleep, 5.0, 7.5));
    }

    [Fact]
    public void EachSignalSpeaksItsOwnUnit()
    {
        Assert.Equal("down 2.2 lb", Say(BodySignal.Weight, 179.2, 181.4));
        Assert.Equal("down 1.5 points", Say(BodySignal.BodyComposition, 19.9, 21.4));
        Assert.Equal("down 4 bpm", Say(BodySignal.RestingHeartRate, 59, 63));
        Assert.Equal("up 7 ms", Say(BodySignal.HeartRateVariability, 45, 38));
        Assert.Equal("up about 1,500 steps a day", Say(BodySignal.Steps, 7400, 5900));
        Assert.Equal("up 12 minutes a day", Say(BodySignal.ActiveMinutes, 42, 30));
        Assert.Equal("up 250 lb", Say(BodySignal.Strength, 1250, 1000));
        Assert.Equal("up about 190 kcal a day", Say(BodySignal.Calories, 2290, 2100));
    }

    [Fact]
    public void AWobbleIsAboutTheSame()
    {
        Assert.Equal("about the same", Say(BodySignal.Weight, 180.5, 180.0));
        Assert.Equal("about the same", Say(BodySignal.Sleep, 7.1, 7.0));
        Assert.Equal("about the same", Say(BodySignal.Steps, 7100, 7000));
    }

    [Fact]
    public void NoPhraseMentionsAPercentOrAnyoneElse()
    {
        foreach (var signal in BodySignals.All)
        {
            foreach (var pair in new[] { (50.0, 80.0), (80.0, 50.0), (8000.0, 2000.0) })
            {
                var words = Say(signal, pair.Item1, pair.Item2);
                Assert.DoesNotContain('%', words);
                Assert.DoesNotContain("average", words, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("normal", words, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("than", words, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ADevicesLanguageDoesNotChangeTheNumbers()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("up about 1,500 steps a day", Say(BodySignal.Steps, 7400, 5900));
            Assert.Equal("down 2.2 lb", Say(BodySignal.Weight, 179.2, 181.4));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void TheChangeSaysItselfInWords()
    {
        var change = BodyData.Change(BodySignal.Weight, 179.2, 181.4);
        Assert.Equal(ChangeWords.Phrase(change), change.Words);
    }

    [Fact]
    public void ANullChangeIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => ChangeWords.Phrase(null!));
    }
}
