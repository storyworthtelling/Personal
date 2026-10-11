using System.Text.RegularExpressions;

namespace Storyworthtelling.Voice.Tests;

// DriveTextFile talks to a real DriveService, which these tests cannot fake, so this guard reads its source:
// every name search must go through DriveFileChooser (owned first, then most recent) rather than taking the
// first of an unordered pageSize=1 listing — including the post-create lookup in WriteAsync.
public class DriveTextFileLookupGuardTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Storyworthtelling.Voice", "DriveTextFile.cs")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "Storyworthtelling.Voice", "DriveTextFile.cs"));
    }

    [Fact]
    public void EveryNameSearchGoesThroughTheChooser()
    {
        var source = Source();
        var listCalls = Regex.Matches(source, @"Files\.List\(\)").Count;

        Assert.True(listCalls > 0, "found no Files.List() call to check — the guard would pass vacuously");
        Assert.DoesNotContain("PageSize = 1", source);
        Assert.DoesNotContain("Files[0]", source);
        Assert.Equal(listCalls, Regex.Matches(source, @"DriveFileChooser\.ListFields").Count);
        Assert.Equal(listCalls, Regex.Matches(source, @"DriveFileChooser\.NameQuery\(").Count);
        Assert.Contains("DriveFileChooser.Choose(", source);
    }
}
