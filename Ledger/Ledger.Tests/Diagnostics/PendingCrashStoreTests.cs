using Index.Domain.Diagnostics;
using Xunit;

namespace Index.Tests.Diagnostics;

public class PendingCrashStoreTests : IDisposable
{
    private readonly string directory;
    private readonly PendingCrashStore store;

    public PendingCrashStoreTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "index-pendingcrash-" + Guid.NewGuid().ToString("N"));
        store = new PendingCrashStore(directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AnEmptyDirectoryHasNoPendingCrash()
    {
        Assert.False(store.HasCrash);
        Assert.Null(store.ReadText());
    }

    [Fact]
    public void ASavedReportIsReadBackWithItsWhereAndItsWholeException()
    {
        var report = LaunchFault.Describe(
            new DateTimeOffset(2026, 10, 10, 9, 15, 0, TimeSpan.Zero),
            "Accounts page, OnAppearing",
            Thrown());

        store.Save(report);

        Assert.True(store.HasCrash);
        var text = store.ReadText();
        Assert.Equal(report, text);
        Assert.Contains("Accounts page, OnAppearing", text);
        Assert.Contains("System.InvalidOperationException: example failure", text);
        Assert.Contains("---> System.ApplicationException: example inner failure", text);
    }

    [Fact]
    public void SavingTwiceKeepsOnlyTheLatestReport()
    {
        store.Save("first");
        store.Save("second");

        Assert.Equal("second", store.ReadText());
    }

    [Fact]
    public void ClearRemovesThePendingCrash()
    {
        store.Save("boom");

        store.Clear();

        Assert.False(store.HasCrash);
        Assert.Null(store.ReadText());
    }

    [Fact]
    public void ClearOnAnEmptyDirectoryDoesNothing()
    {
        store.Clear();

        Assert.False(store.HasCrash);
    }

    [Fact]
    public void DescribeCarriesTheExceptionTypeMessageAndStackOfTheFault()
    {
        var text = LaunchFault.Describe(DateTimeOffset.UnixEpoch, "Where", Thrown());

        Assert.Contains("Where: Where", text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("example failure", text);
    }

    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException(
                "example failure", new ApplicationException("example inner failure"));
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
