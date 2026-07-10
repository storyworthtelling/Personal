using FocusPhone.Domain.Diagnostics;
using Xunit;

namespace FocusPhone.Tests.Diagnostics;

public class CrashLogStoreTests : IDisposable
{
    private readonly string directory;
    private readonly CrashLogStore store;

    public CrashLogStoreTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "focusphone-crashtests-" + Guid.NewGuid().ToString("N"));
        store = new CrashLogStore(directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CrashReport Report(string message = "boom") => new(
        new DateTimeOffset(2026, 7, 10, 9, 15, 0, TimeSpan.Zero),
        "Communication",
        "System.InvalidOperationException",
        message,
        "   at Somewhere()");

    [Fact]
    public void ReadLatest_EmptyDirectory_ReturnsNull()
    {
        Assert.False(store.HasCrash);
        Assert.Null(store.ReadLatest());
    }

    [Fact]
    public void Save_ThenReadLatest_RoundTrips()
    {
        var report = Report();

        store.Save(report);

        Assert.True(store.HasCrash);
        var loaded = store.ReadLatest();
        Assert.NotNull(loaded);
        Assert.Equal(report.When, loaded!.When);
        Assert.Equal(report.AppLabel, loaded.AppLabel);
        Assert.Equal(report.Message, loaded.Message);
    }

    [Fact]
    public void Save_Twice_KeepsOnlyTheLatest()
    {
        store.Save(Report("first"));
        store.Save(Report("second"));

        Assert.Equal("second", store.ReadLatest()!.Message);
    }

    [Fact]
    public void Clear_RemovesTheCrash()
    {
        store.Save(Report());

        store.Clear();

        Assert.False(store.HasCrash);
        Assert.Null(store.ReadLatest());
    }

    [Fact]
    public void Clear_WhenNothingSaved_DoesNotThrow()
    {
        store.Clear();
        Assert.False(store.HasCrash);
    }
}
