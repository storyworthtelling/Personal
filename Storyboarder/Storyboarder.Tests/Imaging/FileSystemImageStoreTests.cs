using Microsoft.Extensions.Options;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.Imaging;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.Imaging;

public class FileSystemImageStoreTests : IDisposable
{
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "storyboarder-imagestore", Guid.NewGuid().ToString("N"));
    private readonly FileSystemImageStore store;

    public FileSystemImageStoreTests()
    {
        store = new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot }));
    }

    public void Dispose()
    {
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    [Fact]
    public void Delete_RemovesAWholeSubtree()
    {
        var novelId = Guid.NewGuid().ToString();
        var first = store.Save($"{novelId}/refs/a.png", SampleImages.Png());
        var second = store.Save($"{novelId}/pages/b.png", SampleImages.Png());

        store.Delete(novelId);

        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.False(Directory.Exists(Path.Combine(outputRoot, novelId)));
    }

    [Fact]
    public void Delete_IsANoOp_WhenNothingIsThere()
    {
        // Deleting a path that was never written must not throw.
        store.Delete(Guid.NewGuid().ToString());
    }
}
