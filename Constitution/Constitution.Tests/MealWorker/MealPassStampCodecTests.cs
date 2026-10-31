using Constitution.Domain.Meals.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWorker.MealWorkerData;

namespace Constitution.Tests.MealWorker;

public class MealPassStampCodecTests
{
    [Fact]
    public void AStampRoundTrips()
    {
        var stamp = new MealPassStamp(Edt(10, 26, 7, 5), NextSunday, Edt(10, 26, 7, 4), Edt(10, 25, 8, 6));

        var read = Assert.IsType<MealPassStampRead.Held>(MealPassStampCodec.Decode(MealPassStampCodec.Encode(stamp)));

        Assert.Equal(stamp, read.Stamp);
    }

    [Fact]
    public void AnEmptyStampRoundTrips()
    {
        var read = Assert.IsType<MealPassStampRead.Held>(MealPassStampCodec.Decode(MealPassStampCodec.Encode(MealPassStamp.Empty)));

        Assert.Equal(MealPassStamp.Empty, read.Stamp);
    }

    [Fact]
    public void TheNamesOnDiskAreFrozen()
    {
        var text = MealPassStampCodec.Encode(new MealPassStamp(Edt(10, 26, 7, 5), NextSunday, Edt(10, 26, 7, 4), Edt(10, 25, 8, 6)));

        foreach (var name in new[] { "\"version\":1", "\"planSettledAt\":", "\"planWeek\":\"2026-11-01\"", "\"planTriedAt\":", "\"deadlineTriedAt\":" })
        {
            Assert.Contains(name, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NothingSavedIsNothingYet()
    {
        Assert.IsType<MealPassStampRead.NothingYet>(MealPassStampCodec.Decode(null));
        Assert.IsType<MealPassStampRead.NothingYet>(MealPassStampCodec.Decode("   "));
    }

    [Theory]
    [InlineData("this is not json", MealPassStampCodec.NotJson)]
    [InlineData("[]", MealPassStampCodec.WrongShape)]
    [InlineData("{\"version\":2}", MealPassStampCodec.WrongShape)]
    [InlineData("{\"planWeek\":\"2026-11-01\"}", MealPassStampCodec.WrongShape)]
    [InlineData("{\"version\":1,\"planWeek\":\"next tuesday\"}", MealPassStampCodec.WrongShape)]
    [InlineData("{\"version\":1,\"planTriedAt\":\"yesterday\"}", MealPassStampCodec.WrongShape)]
    public void ATextThatIsNotAStampIsUnreadableWithAFixedSentence(string text, string sentence)
    {
        var read = Assert.IsType<MealPassStampRead.Unreadable>(MealPassStampCodec.Decode(text));

        Assert.Equal(sentence, read.Reason);
    }
}
