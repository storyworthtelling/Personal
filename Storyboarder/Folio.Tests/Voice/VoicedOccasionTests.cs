using Folio.Domain.Voice;

namespace Folio.Tests.Voice;

/// <summary>
/// What Folio is willing to say in the owner's voice — and the rule that it never says a failure in
/// it.
///
/// <para><b>The error rule.</b> A voice is for the app's own remarks, not for its bad news. A failed
/// review, an expired key, a Drive that will not answer: these are facts the owner has to act on, and
/// dressing them in their own wit makes them read as banter about their book rather than as something
/// wrong — and, worse, it puts a model call on the path of telling someone that a model call failed.
/// Errors stay in plain copy.</para>
///
/// <para>The bug this reintroduces is voicing one. It is an easy and appealing mistake — the failure
/// message is exactly the copy that feels most improved by sounding human — so the occasions are a
/// closed list rather than a convention, and a new one has to be added deliberately.</para>
/// </summary>
public class VoicedOccasionTests
{
    private static readonly string[] Trouble =
        ["error", "fail", "failed", "failure", "problem", "sorry", "unable", "denied", "invalid", "expired", "crash"];

    [Fact]
    public void NoOccasionIsAFailure()
    {
        foreach (var occasion in VoicedOccasions.All)
        {
            var offending = Trouble.FirstOrDefault(word =>
                occasion.Contains(word, StringComparison.OrdinalIgnoreCase));

            Assert.True(offending is null,
                $"The occasion \"{occasion}\" reads as trouble (\"{offending}\"). Errors are told in "
                + "plain copy: a voiced failure sounds like banter about the owner's book, and puts a "
                + "model call on the path of reporting that a model call failed.");
        }
    }

    [Fact]
    public void TheOccasionsAreDistinct()
    {
        Assert.Equal(VoicedOccasions.All.Count, VoicedOccasions.All.Distinct().Count());
    }

    /// <summary>
    /// Every voiced surface in the app names its occasion from the list. Source-level, because the
    /// next voiced surface will be written by someone reaching for the nearest constructor, and a
    /// bare string there is how an error message quietly becomes voiced.
    /// </summary>
    [Fact]
    public void EveryVoicedSurfaceNamesAnOccasionFromTheList()
    {
        var root = Path.Combine(RepoRoot(), "Storyboarder");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}Folio.Tests{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            foreach (var line in Strip(File.ReadAllText(file)).Split('\n'))
            {
                var at = line.IndexOf("new MessageRequest(", StringComparison.Ordinal);
                if (at < 0)
                    continue;

                var argument = line[(at + "new MessageRequest(".Length)..].TrimStart();
                if (argument.Length > 0 && !argument.StartsWith("VoicedOccasions.", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These voice a message without naming an occasion from VoicedOccasions:\n  "
            + string.Join("\n  ", offenders));
    }

    private static string Strip(string source) => string.Join('\n',
        source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DESIGN.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
