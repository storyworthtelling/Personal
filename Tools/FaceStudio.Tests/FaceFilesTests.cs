namespace FaceStudio.Tests;

/// <summary>The committed face is the tool's output, never hand-edited, and carries the watch's own font.</summary>
public class FaceFilesTests
{
    [Fact]
    public void TheCommittedFaceIsCurrent()
    {
        var root = FaceRead.RepoRoot();
        var folder = Path.Combine(root, FaceFiles.Folder);
        var generated = FaceFiles.All(root);

        foreach (var file in generated)
        {
            var path = Path.Combine(folder, file.Path);
            Assert.True(File.Exists(path), $"{file.Path} is not committed; run FaceStudio.");
            Assert.True(FaceFiles.Same(file.Path, file.Bytes, File.ReadAllBytes(path)), $"{file.Path} differs from what FaceStudio makes.");
        }

        var committed = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .Where(path => !path.StartsWith("bin/", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(generated.Select(file => file.Path).Order(), committed.Order());
    }

    [Fact]
    public void PlexMonoIsEmbedded()
    {
        var root = FaceRead.RepoRoot();
        var font = FaceFiles.All(root).Single(file => file.Path == $"res/font/{PointFace.FontResource}.ttf");

        Assert.Equal(File.ReadAllBytes(Path.Combine(root, "Watch", "Fonts", PointFace.FontFile)), font.Bytes);
        Assert.True(File.Exists(Path.Combine(root, "Watch", "Fonts", "OFL.txt")), "The font's licence travels with it.");
    }

    [Fact]
    public void EveryMaskIsShipped()
    {
        var paths = FaceFiles.All(FaceRead.RepoRoot()).Select(file => file.Path).ToHashSet();
        var images = FaceRead.Face.Descendants("Image").Select(image => (string)image.Attribute("resource")!).Distinct().ToList();

        Assert.Equal(20, images.Count);
        Assert.All(images, name => Assert.Contains($"res/drawable-nodpi/{name}.png", paths));
        Assert.Contains("res/drawable-nodpi/preview.png", paths);
    }
}
