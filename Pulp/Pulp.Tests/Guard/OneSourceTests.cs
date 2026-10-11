using System.Text.RegularExpressions;

namespace Pulp.Tests.Guard;

/// <summary>
/// Release C's four screens were built in parallel, and each answered three shared questions
/// privately: whose phone this is, how a poster is fetched, and how the catalogue is searched. The
/// private answers drifted — one "whose phone" took the first adult (D97), the posters were kept
/// under three names in two folders, and two search clients ran outside the Diagnostics lookup count
/// (D261). Each question now has one answer, and these guards keep a second from growing back. Each
/// asserts it found the one answer it protects, so none passes on an empty tree.
/// </summary>
public class OneSourceTests
{
    private static IReadOnlyList<(string Path, string Text)> Sources(string project) =>
        PulpSource.Files(".cs")
            .Select(file => (Path: PulpSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Path.StartsWith(project + "/", StringComparison.Ordinal))
            .ToList();

    private static void OnlyIn(string project, Regex pattern, Func<string, bool> allowed, string what)
    {
        var hits = Sources(project).Where(file => pattern.IsMatch(file.Text)).Select(file => file.Path).ToList();

        Assert.True(hits.Any(allowed), $"The one place that {what} was not found; the guard has nothing to protect.");
        var strays = hits.Where(path => !allowed(path)).ToList();
        Assert.True(strays.Count == 0, $"Only one place {what}; also found in: {string.Join(", ", strays)}");
    }

    [Fact]
    public void OnlyTheCompositionRootBuildsTheAvailabilityClient() =>
        OnlyIn(
            "Pulp.Android",
            new Regex(@"new\s+StreamingAvailabilityClient\s*\("),
            path => path.StartsWith("Pulp.Android/App/", StringComparison.Ordinal),
            "builds a Streaming Availability client (so every lookup is counted, D261)");

    [Fact]
    public void OnlyTheCompositionRootMakesAnHttpClient() =>
        OnlyIn(
            "Pulp.Android",
            new Regex(@"new\s+HttpClient\s*\(|HttpClient\s+\w+\s*=\s*new\s*\("),
            path => path.StartsWith("Pulp.Android/App/", StringComparison.Ordinal),
            "makes an HttpClient");

    [Fact]
    public void OnlyPostersDownloadsAPoster() =>
        OnlyIn(
            "Pulp.Android",
            new Regex(@"GetByteArrayAsync|GetStreamAsync"),
            path => path == "Pulp.Android/Components/Posters.cs",
            "downloads a poster");

    /// <summary>Picking a viewer by "not a child" is the shape every private copy of "whose phone" took.</summary>
    [Fact]
    public void OnlyThisPhoneDecidesWhosePhoneItIs()
    {
        var pattern = new Regex(@"!\s*\w+\.IsChild\b(?!\s*\()");
        OnlyIn("Pulp.Domain", pattern, path => path == "Pulp.Domain/People/ThisPhone.cs", "decides whose phone this is");
        Assert.DoesNotContain(Sources("Pulp.Android"), file => pattern.IsMatch(file.Text));
    }

    [Fact]
    public void NoScreenKeepsItsOwnViewerResolver()
    {
        var resolver = new Regex(@"\bclass\s+\w*Viewer\b");
        var found = Sources("Pulp.Domain").Concat(Sources("Pulp.Android"))
            .Where(file => resolver.IsMatch(file.Text))
            .Select(file => file.Path)
            .ToList();

        Assert.True(found.Count == 0, "A screen's own viewer resolver: " + string.Join(", ", found));
        Assert.Contains(Sources("Pulp.Domain"), file => file.Path == "Pulp.Domain/People/ThisPhone.cs");
    }
}
