using System.Text.RegularExpressions;

namespace Pulp.Tests.Guard;

/// <summary>
/// G6 — Pulp plays nothing itself. It finds what to watch and hands off to the service's own app;
/// in-app playback is ruled out by the services' terms and by DRM (finding §2.1). A player package
/// or a protected-media grant (<c>RESOURCE_PROTECTED_MEDIA_ID</c>, whose wire value is <c>android.webkit.resource.PROTECTED_MEDIA_ID</c>) arriving here would be the first step down a road that cannot end in
/// a working app, and nothing about it would fail until a device refused to decode a frame.
/// </summary>
public class PlaysNothingTests
{
    private static readonly Regex Forbidden = new(
        @"Media3|ExoPlayer|Android\.Media\.MediaPlayer|\bMediaPlayer\b|\bVideoView\b|PROTECTED_MEDIA_ID|ProtectedMediaId",
        RegexOptions.IgnoreCase);

    [Fact]
    public void NoPlaybackPathExistsInPulp()
    {
        var files = PulpSource.Files(".cs", ".csproj", ".xml");
        Assert.True(files.Count > 0, "PlaysNothingTests scanned no files; the guard would pass on nothing.");
        Assert.Contains(files, file => file.EndsWith(".csproj", StringComparison.Ordinal));

        var offenders = files
            .Where(file => !PulpSource.IsGuard(file, "PlaysNothingTests.cs"))
            .Select(file => (file, match: Forbidden.Match(File.ReadAllText(file))))
            .Where(hit => hit.match.Success)
            .Select(hit => $"{PulpSource.Relative(hit.file)}: {hit.match.Value}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Pulp gained a playback path of its own (it hands off; it never plays):\n" + string.Join("\n", offenders));
    }
}
