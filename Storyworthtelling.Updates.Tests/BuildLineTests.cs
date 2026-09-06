namespace Storyworthtelling.Updates.Tests;

public class BuildLineTests
{
    [Fact]
    public void ReadsAsNameVersionPackage()
    {
        Assert.Equal("Flyleaf 1.0.163 · com.storyworthtelling.focusphone.launcher",
            BuildLine.Format("Flyleaf", "1.0.163", "com.storyworthtelling.focusphone.launcher"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnreadableVersionSaysSoRatherThanLookingBlank(string? versionName)
    {
        // The package manager can refuse, and a line that silently drops the version reads as
        // though the app has none rather than as though the lookup failed.
        Assert.Equal("Score ? · com.storyworthtelling.focusphone.audio",
            BuildLine.Format("Score", versionName, "com.storyworthtelling.focusphone.audio"));
    }

    [Fact]
    public void AMissingPackageDropsTheInterpunctRatherThanTrailingIt()
    {
        Assert.Equal("Ledger 2026.09.06", BuildLine.Format("Ledger", "2026.09.06", null));
    }

    [Fact]
    public void SurroundingSpaceIsTrimmed()
    {
        Assert.Equal("Post 1.0 · com.storyworthtelling.focusphone.communication",
            BuildLine.Format("  Post ", " 1.0 ", " com.storyworthtelling.focusphone.communication "));
    }

    [Fact]
    public void MetadataIsJoinedByAnInterpunct()
    {
        // DESIGN.md §14: interpuncts join metadata, em-dashes join clause-level status.
        var line = BuildLine.Format("Canonizer", "1.0.169", "com.storyworthtelling.storyboarder");

        Assert.Contains(" · ", line);
        Assert.DoesNotContain("—", line);
        Assert.DoesNotContain(" - ", line);
    }
}
