namespace Pulp.Tests.Infrastructure;

/// <summary>
/// Two screens asking for one uncached poster at once used to write the same <c>&lt;name&gt;.part</c>
/// file: one fetch's move could put the other's half-written bytes in place, and a reader could
/// decode a torn file. Each fetch writes its own temporary file and moves it into place whole.
/// <c>Posters</c> lives in the Android head, so this reads its source.
/// </summary>
public class PosterTempFileTests
{
    private static string Source()
    {
        var file = Path.Combine(PulpSource.Root(), "Pulp.Android", "Components", "Posters.cs");
        Assert.True(File.Exists(file), "Posters.cs was not found at " + file);
        return File.ReadAllText(file);
    }

    [Fact]
    public void EachFetchWritesItsOwnTemporaryFile()
    {
        var source = Source();

        Assert.Contains("WriteAllBytesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("path + \".part\"", source, StringComparison.Ordinal);
        Assert.Contains("Guid.NewGuid()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFinishedFileIsMovedIntoPlace()
    {
        var source = Source();

        Assert.Contains("File.Move(", source, StringComparison.Ordinal);
    }
}
