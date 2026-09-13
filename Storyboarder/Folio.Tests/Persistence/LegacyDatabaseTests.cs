using Folio.Infrastructure.Persistence;
using Xunit;

namespace Folio.Tests.Persistence;

public class LegacyDatabaseTests
{
    [Fact]
    public void FilesInCoversTheDatabaseAndBothSqliteSiblings()
    {
        var files = LegacyDatabase.FilesIn("/data").Select(Path.GetFileName).ToList();

        Assert.Equal(["storyboarder.db", "storyboarder.db-wal", "storyboarder.db-shm"], files);
    }

    [Fact]
    public void RemoveDeletesEveryFileItNames()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            foreach (var file in LegacyDatabase.FilesIn(dir))
            {
                File.WriteAllText(file, "legacy");
            }

            LegacyDatabase.Remove(dir);

            Assert.All(LegacyDatabase.FilesIn(dir), file => Assert.False(File.Exists(file)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // A fresh install never had one. Absence is the ordinary case, not a failure.
    [Fact]
    public void RemoveIgnoresADirectoryWithNoLegacyDatabase()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            LegacyDatabase.Remove(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
