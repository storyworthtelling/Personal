using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// D153 (independent review): the picture an errand takes for another app is a cache file between the shutter and the caller's destination, and it is deleted once it has been delivered, failed or abandoned.
/// The activity cannot be loaded here, so this reads its source; what it cannot show is a device cache after a real capture, which is not verified.
/// </summary>
public class ErrandCaptureCleanupTests
{
    private static string Errand()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CLAUDE.md")))
        {
            root = Path.GetDirectoryName(root);
        }

        return File.ReadAllText(Path.Combine(root!, "FocusPhone", "Plate.Android", "ErrandActivity.cs"));
    }

    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " is gone");
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            depth += source[index] == '{' ? 1 : source[index] == '}' ? -1 : 0;
            if (depth == 0)
            {
                return source[open..(index + 1)];
            }
        }

        throw new InvalidOperationException("unbalanced");
    }

    [Fact]
    public void TheCaptureFileHasOneNameAndIsNotSpelledTwice()
    {
        var source = Errand();

        Assert.Contains("CaptureFrameName = \"errand-capture.jpg\"", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("\"errand-capture.jpg\"").Length - 1);
    }

    [Fact]
    public void DeliveringDeletesTheCaptureWhateverBecameOfTheCopy()
    {
        var delivered = Body(Errand(), "private void Delivered(");

        Assert.Contains("finally", delivered, StringComparison.Ordinal);
        Assert.Contains("DiscardFrame(file)", delivered[delivered.IndexOf("finally", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void ACaptureThatFailedToSaveIsDeleted()
    {
        var failed = Body(Errand(), "public void OnError(");

        Assert.Contains("DiscardFrame(file)", failed, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavingTheScreenDeletesTheCaptureToo()
    {
        var destroyed = Body(Errand(), "protected override void OnDestroy()");

        Assert.Contains("CaptureFrameName", destroyed, StringComparison.Ordinal);
        Assert.Contains("ReadFrameName", destroyed, StringComparison.Ordinal);
    }

    [Fact]
    public void NoDeleteIsSwallowedSilently()
    {
        var discard = Body(Errand(), "private void DiscardFrame(");

        Assert.Contains("Log.Warn", discard, StringComparison.Ordinal);
    }
}
