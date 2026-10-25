using Constitution.Domain.Meals;
using Constitution.Infrastructure.Meals;
using static Constitution.Tests.AteSomethingElse.AteFixture;

namespace Constitution.Tests.AteSomethingElse;

/// <summary>The estimate record lives in one small local file: a codec, replacement per day and meal, and a file that cannot be read.</summary>
public class AteStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "ate-store-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(folder, "intake-estimates.json");

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheCodecRoundTrips()
    {
        var items = new[] { Estimate(), Estimate(AteMeal.Lunch, 300, 450) };

        var read = AteCodec.Read(AteCodec.Write(items));

        Assert.False(read.Unreadable);
        Assert.Equal(items, read.Items);
    }

    [Fact]
    public void TheWireNamesAreWrittenOutNotDerived()
    {
        var json = AteCodec.Write([Estimate()]);

        foreach (var name in new[] { "\"version\"", "\"day\"", "\"meal\"", "\"what\"", "\"kcalLow\"", "\"kcalHigh\"", "\"proteinLow\"", "\"proteinHigh\"", "\"confidence\"" })
        {
            Assert.Contains(name, json, StringComparison.Ordinal);
        }

        Assert.Contains("\"dinner\"", json, StringComparison.Ordinal);
        Assert.Contains("2026-11-17", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoFileIsAnEmptyRecordNotAFault(string? text)
    {
        var read = AteCodec.Read(text);

        Assert.Empty(read.Items);
        Assert.False(read.Unreadable);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":99,\"items\":[]}")]
    [InlineData("{\"version\":1,\"items\":[{\"day\":\"2026-11-17\",\"meal\":\"dinner\",\"what\":\"x\",\"kcalLow\":0,\"kcalHigh\":0,\"confidence\":\"low\"}]}")]
    [InlineData("{\"version\":1,\"items\":[{\"day\":\"never\",\"meal\":\"dinner\",\"what\":\"x\",\"kcalLow\":300,\"kcalHigh\":400,\"confidence\":\"low\"}]}")]
    [InlineData("{\"version\":1,\"items\":[{\"day\":\"2026-11-17\",\"meal\":\"brunch\",\"what\":\"x\",\"kcalLow\":300,\"kcalHigh\":400,\"confidence\":\"low\"}]}")]
    public void AFileThatCannotBeReadIsSaidSoAndNeverReadAsZero(string text)
    {
        var read = AteCodec.Read(text);

        Assert.True(read.Unreadable);
        Assert.Empty(read.Items);
    }

    [Fact]
    public void ARecordIsReplacedPerDayAndMeal()
    {
        var store = new FileAteEstimates(FilePath);

        Assert.Null(store.Record(Estimate(low: 750, high: 1050)));
        var previous = store.Record(Estimate(low: 1200, high: 1500));

        Assert.Equal(750, previous!.Reading.KcalLow);
        var items = store.Read().Items;
        Assert.Equal(1200, Assert.Single(items).Reading.KcalLow);
    }

    [Fact]
    public void WithdrawingPutsThePreviousBack()
    {
        var store = new FileAteEstimates(FilePath);
        var first = Estimate(low: 750, high: 1050);
        store.Record(first);
        var second = Estimate(low: 1200, high: 1500);
        var previous = store.Record(second);

        store.Withdraw(second, previous);

        Assert.Equal(first, Assert.Single(store.Read().Items));
    }

    [Fact]
    public void WithdrawingWithNothingBeforeLeavesNone()
    {
        var store = new FileAteEstimates(FilePath);
        var only = Estimate();
        store.Record(only);

        store.Withdraw(only, null);

        Assert.Empty(store.Read().Items);
    }

    [Fact]
    public void ARecordSurvivesANewStoreOnTheSameFile()
    {
        new FileAteEstimates(FilePath).Record(Estimate());

        Assert.Single(new FileAteEstimates(FilePath).Read().Items);
    }

    [Fact]
    public void AnUnreadableFileIsKeptAsideNotWrittenOver()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath, "{garbled");
        var store = new FileAteEstimates(FilePath);

        Assert.True(store.Read().Unreadable);
        store.Record(Estimate());

        Assert.False(store.Read().Unreadable);
        Assert.Single(store.Read().Items);
        var aside = Assert.Single(Directory.GetFiles(folder, "*.unreadable"));
        Assert.Equal("{garbled", File.ReadAllText(aside));
    }

    [Fact]
    public void OldEstimatesAreLetGoAfterSixtyDays()
    {
        var store = new FileAteEstimates(FilePath);
        store.Record(new AteEstimate(new DateOnly(2026, 1, 1), AteMeal.Dinner, Reading()));

        store.Record(Estimate());

        Assert.Equal(new DateOnly(2026, 11, 17), Assert.Single(store.Read().Items).Day);
    }

    [Fact]
    public void NoTempFileIsLeftBehind()
    {
        new FileAteEstimates(FilePath).Record(Estimate());

        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }
}
