using System.Text.RegularExpressions;
using Codex.Tests.Design;
using Xunit;

namespace Codex.Tests.Time;

/// <summary>
/// The four Domains carry byte-parallel copies of the time helpers (the repo
/// pattern: duplicate small pure code, keep it honest with a parity test).
/// </summary>
public class TimeHelperParityTests
{
    [Theory]
    [InlineData("Time/RelativeTime.cs")]
    [InlineData("Time/TimeFormats.cs")]
    public void AllFourCopiesAreIdenticalModuloNamespace(string relativePath)
    {
        var repoRoot = Directory.GetParent(DesignTestHelpers.FocusPhoneRoot())!.FullName;
        var copies = new[]
        {
            Path.Combine(repoRoot, "FocusPhone", "Codex.Domain", relativePath),
            Path.Combine(repoRoot, "Storyboarder", "Folio.Domain", relativePath),
            Path.Combine(repoRoot, "Foreword", "Foreword.Domain", relativePath),
            Path.Combine(repoRoot, "Constitution", "Constitution.Domain", relativePath),
        };
        var normalized = copies
            .Select(File.ReadAllText)
            .Select(source => Regex.Replace(source, @"namespace [\w.]+;", "namespace NORMALIZED;"))
            .ToList();
        Assert.Equal(normalized[0], normalized[1]);
        Assert.Equal(normalized[0], normalized[2]);
        Assert.Equal(normalized[0], normalized[3]);
    }
}
