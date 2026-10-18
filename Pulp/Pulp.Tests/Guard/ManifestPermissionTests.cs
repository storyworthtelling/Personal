namespace Pulp.Tests.Guard;

/// <summary>
/// A signed Release build carries only the permissions the head declares. Debug adds INTERNET on
/// its own, which hides an omission until the first signed build, and then every availability,
/// sports, calendar and vault call fails with nothing on screen to say why.
/// </summary>
public sealed class ManifestPermissionTests
{
    [Fact]
    public void TheHeadDeclaresInternet()
    {
        var sources = PulpSource.Files(".cs")
            .Where(file => PulpSource.Relative(file).Contains("Pulp.Android", StringComparison.Ordinal))
            .ToList();
        Assert.True(sources.Count >= 10, "The scan should cover the head's sources, found " + sources.Count);

        var declared = sources.Any(file => File.ReadAllText(file)
            .Contains("[assembly: UsesPermission(Android.Manifest.Permission.Internet)]", StringComparison.Ordinal));
        Assert.True(declared, "Declare INTERNET on the head: [assembly: UsesPermission(Android.Manifest.Permission.Internet)].");
    }

    [Fact]
    public void TheWorkflowChecksTheBuiltApkForInternet()
    {
        var workflow = Path.Combine(PulpSource.Root(), "..", ".github", "workflows", "build-pulp.yml");
        Assert.True(File.Exists(workflow), "build-pulp.yml was not found at " + workflow);
        var text = File.ReadAllText(workflow);
        Assert.Contains("INTERNET", text, StringComparison.Ordinal);
        Assert.Contains("dump permissions", text, StringComparison.Ordinal);
    }
}
