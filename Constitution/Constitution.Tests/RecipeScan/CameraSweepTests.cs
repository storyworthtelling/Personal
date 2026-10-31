using Constitution.Domain.Recipes.Screens;
using Constitution.Tests.Diagnostics;

namespace Constitution.Tests.RecipeScan;

/// <summary>D162: a photograph the phone left behind is cleared when the app starts, not only when the next capture begins.</summary>
public sealed class CameraSweepTests : IDisposable
{
    private readonly string cache = Path.Combine(Path.GetTempPath(), "sweep-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(cache))
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    private static bool Remove(string path)
    {
        File.Delete(path);
        return true;
    }

    [Fact]
    public void EveryFileInTheCaptureFolderGoes()
    {
        Directory.CreateDirectory(CameraCaptureCache.FolderIn(cache));
        File.WriteAllText(CameraCaptureCache.PathIn(cache), "invented page");
        File.WriteAllText(Path.Combine(CameraCaptureCache.FolderIn(cache), "stray.jpg"), "invented stray");

        var stayed = CameraCaptureCache.Sweep(cache, Remove);

        Assert.Empty(stayed);
        Assert.Empty(Directory.GetFiles(CameraCaptureCache.FolderIn(cache)));
    }

    [Fact]
    public void NothingOutsideTheCaptureFolderIsTouched()
    {
        Directory.CreateDirectory(CameraCaptureCache.FolderIn(cache));
        var other = Path.Combine(cache, "keep.txt");
        File.WriteAllText(other, "invented");
        File.WriteAllText(CameraCaptureCache.PathIn(cache), "invented page");

        CameraCaptureCache.Sweep(cache, Remove);

        Assert.True(File.Exists(other));
    }

    [Fact]
    public void AMissingFolderIsNothingToDo()
    {
        Assert.Empty(CameraCaptureCache.Sweep(cache, Remove));
    }

    [Fact]
    public void AFileThatCannotBeDeletedIsNamedNotReportedGone()
    {
        Directory.CreateDirectory(CameraCaptureCache.FolderIn(cache));
        File.WriteAllText(CameraCaptureCache.PathIn(cache), "invented page");

        var stayed = CameraCaptureCache.Sweep(cache, _ => false);

        Assert.Equal([CameraCaptureCache.PathIn(cache)], stayed);
    }

    [Fact]
    public void TheAppSweepsAtStartAndTheLauncherSweepsThroughTheSameRule()
    {
        var activity = File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => file.EndsWith("Constitution.Android/ConstitutionActivity.cs", StringComparison.Ordinal)));
        var launcher = File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => file.EndsWith("MealsTab/CameraCaptureLauncher.cs", StringComparison.Ordinal)));
        var create = activity[activity.IndexOf("protected override void OnCreate(", StringComparison.Ordinal)..];

        Assert.Contains("CameraCaptureLauncher.SweepLeftovers(", create[..create.IndexOf("RefreshTabs", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("CameraCaptureCache.Sweep(", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("ListFiles()", launcher, StringComparison.Ordinal);
    }
}
