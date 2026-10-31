using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Meals;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWorker.MealWorkerData;

namespace Constitution.Tests.MealWorker;

public sealed class FileMealPassStampsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "meal-pass-" + Guid.NewGuid().ToString("N"));

    public FileMealPassStampsTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, true);

    private string StampPath => Path.Combine(folder, "pass-stamp.json");

    [Fact]
    public void NoFileIsNothingYet()
    {
        Assert.IsType<MealPassStampRead.NothingYet>(new FileMealPassStamps(StampPath).Load());
    }

    [Fact]
    public void ASavedStampReadsBack()
    {
        var stamp = new MealPassStamp(Edt(10, 26, 7, 5), NextSunday, null, Edt(10, 25, 8, 6));
        var store = new FileMealPassStamps(StampPath);

        store.Save(stamp);

        Assert.Equal(stamp, Assert.IsType<MealPassStampRead.Held>(new FileMealPassStamps(StampPath).Load()).Stamp);
    }

    [Fact]
    public void AGarbledFileIsUnreadableAndASaveReplacesIt()
    {
        File.WriteAllText(StampPath, "{ not a stamp");
        var store = new FileMealPassStamps(StampPath);

        Assert.IsType<MealPassStampRead.Unreadable>(store.Load());
        store.Save(MealPassStamp.Empty);

        Assert.IsType<MealPassStampRead.Held>(store.Load());
    }

    [Fact]
    public void AFolderWhereTheFileShouldBeIsUnreadableWithTheFixedSentence()
    {
        Directory.CreateDirectory(StampPath);

        var read = Assert.IsType<MealPassStampRead.Unreadable>(new FileMealPassStamps(StampPath).Load());

        Assert.Equal(FileMealPassStamps.UnopenableReason, read.Reason);
    }

    [Fact]
    public void ABlankPathIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new FileMealPassStamps(" "));
    }
}
