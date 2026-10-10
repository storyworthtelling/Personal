using System.Text.RegularExpressions;

namespace Pulp.Tests.Guard;

/// <summary>
/// G6 — Pulp plays nothing itself. It finds what to watch and hands off to the service's own app;
/// in-app playback is ruled out by the services' terms and by DRM (finding §2.1). A player package
/// or a protected-media grant (<c>RESOURCE_PROTECTED_MEDIA_ID</c>, whose wire value is <c>android.webkit.resource.PROTECTED_MEDIA_ID</c>) arriving here would be the first step down a road that cannot end in
/// a working app, and nothing about it would fail until a device refused to decode a frame.
///
/// <para>The one exception is a YouTube trailer (D34, plan P2), played through the shared web
/// view's <c>Player</c> posture — script on, nothing navigates, never autoplays. It is loaded from
/// exactly one class, <c>Pulp.Android/Trailer/TrailerActivity.cs</c>; a second place reaching the
/// player is a second player, and that is what the second test refuses.</para>
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

    /// <summary>The one class allowed to host the trailer player, relative to the folder holding Pulp.sln.</summary>
    public const string TrailerHost = "Pulp.Android/Trailer/TrailerActivity.cs";

    // Any way into the player: the posture itself, the shared loader, the embed builder, or a
    // web view of Pulp's own (which would be a player configured somewhere other than the one place).
    private static readonly Regex PlayerUse = new(
        @"WebPosture\.Player\b|\bLoadPlayer\b|\bPlayerEmbed\b|\bWebView\b");

    [Fact]
    public void TheTrailerPlayerIsUsedFromExactlyOneClass()
    {
        var files = PulpSource.Files(".cs");
        Assert.True(files.Count > 0, "PlaysNothingTests scanned no source; the guard would pass on nothing.");

        // The app's own source only: the tests that read TrailerActivity by content have to name
        // what they look for, and naming the player is not playing it.
        var users = files
            .Select(file => (file, relative: PulpSource.Relative(file)))
            .Where(hit => !hit.relative.StartsWith("Pulp.Tests/", StringComparison.Ordinal))
            .Where(hit => PlayerUse.IsMatch(File.ReadAllText(hit.file)))
            .Select(hit => hit.relative)
            .ToList();

        Assert.True(users.Contains(TrailerHost),
            $"The guard found no use of the Player posture in {TrailerHost}; it would pass on nothing. Found: "
            + string.Join(", ", users));
        Assert.True(users.Count == 1,
            "The trailer player is reached from more than one place (the only player is TrailerActivity):\n"
            + string.Join("\n", users));
    }
}
