using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// <c>StreamMediaDataSource</c> is a managed subclass of Java's <c>MediaDataSource</c>, and
/// <c>MediaExtractor</c> releases it on its own schedule: the platform calls <c>close()</c>
/// back into the object after the managed side has already disposed it. .NET for Android then
/// has to rebuild the managed peer from the native handle, and it does that through a
/// <c>(IntPtr, JniHandleOwnership)</c> constructor. Without one the callback throws
/// <c>NotSupportedException</c> ("Unable to activate instance of type … from native handle") on
/// the Java thread, and the whole app crashes while a podcast plays. The runtime cannot be
/// exercised here, so this reads the source the way the other guards do.
/// </summary>
public class StreamMediaDataSourceTests
{
    private static readonly string SourcePath = Path.Combine(
        RepoRoot(), "FocusPhone", "Score.Android", "AudioLevelReader.cs");

    [Fact]
    public void StreamMediaDataSourceDeclaresTheHandleConstructorThatJavaCallbacksRebuildItThrough()
    {
        var source = File.ReadAllText(SourcePath);

        Assert.Matches(
            new Regex(@"protected\s+StreamMediaDataSource\s*\(\s*nint\s+handle\s*,\s*JniHandleOwnership\s+transfer\s*\)\s*:\s*base\s*\(\s*handle\s*,\s*transfer\s*\)"),
            source);
    }

    [Fact]
    public void RebuiltPeerWithNoStreamReportsEndOfStreamAndDoesNotThrow()
    {
        // The rebuilt peer carries no stream, so its reads must degrade to end-of-stream and
        // its close must be a no-op rather than dereferencing a missing stream.
        var source = File.ReadAllText(SourcePath);

        Assert.Matches(new Regex(@"private\s+readonly\s+System\.IO\.Stream\?\s+stream\s*;"), source);
        Assert.Matches(new Regex(@"stream\s+is\s+null"), source);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repo root (.github) above " + AppContext.BaseDirectory);
    }
}
