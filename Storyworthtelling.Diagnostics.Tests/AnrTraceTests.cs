using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Diagnostics.Tests;

/// <summary>
/// Audit 08 §36: an ANR was logged as a reason and nothing else, though Android keeps the thread dump. The main thread's
/// section is what says where the app was stuck; the rest of the dump is every other thread and is left behind.
/// </summary>
public class AnrTraceTests
{
    private const string Dump =
        "----- pid 4242 at 2026-10-30 09:15:05 -----\n" +
        "Cmd line: com.example.app\n\n" +
        "\"main\" prio=5 tid=1 Blocked\n" +
        "  | group=\"main\" sCount=1\n" +
        "  at com.example.app.Store.read(Store.java:42)\n" +
        "  - waiting to lock <0x0abc> held by thread 12\n" +
        "  at com.example.app.Screen.onResume(Screen.java:7)\n\n" +
        "\"RenderThread\" daemon prio=7 tid=13 Native\n" +
        "  at nativePollOnce(Native method)\n";

    [Fact]
    public void TheMainThreadIsKeptAndTheRestLeft()
    {
        var excerpt = AnrTrace.MainThread(Dump, 4096);

        Assert.StartsWith("\"main\" prio=5 tid=1 Blocked", excerpt, StringComparison.Ordinal);
        Assert.Contains("Store.read(Store.java:42)", excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderThread", excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongSectionIsCut() =>
        Assert.True(AnrTrace.MainThread(Dump, 60).Length <= 60 + "\n… cut".Length);

    [Fact]
    public void ADumpWithNoMainThreadGivesNothing() =>
        Assert.Equal(string.Empty, AnrTrace.MainThread("no threads here", 4096));

    [Fact]
    public void TheDumpIsReadFromTheStreamAndroidKept()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Dump));

        Assert.StartsWith("\"main\"", AnrTrace.FromStream(stream), StringComparison.Ordinal);
        Assert.Equal(string.Empty, AnrTrace.FromStream(null));
    }
}
