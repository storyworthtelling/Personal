using System.Text.RegularExpressions;

namespace Constitution.Tests.DigestPages;

/// <summary>
/// G24 — a correlation appears nowhere in Constitution except the digest's patterns block (C-2.6.2):
/// not on a Body screen, not in a voice seat, not in a notice. Read by content over every source file.
/// </summary>
public class CorrelationsOnlyInTheDigestTests
{
    private static readonly Regex Mention = new(@"\bCorrelation\b|\bPearson\b|\bDigestPatterns\b|\bcorrelat\w*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [Fact]
    public void OnlyTheDigestFolderNamesACorrelation()
    {
        var files = ConstitutionSource.Files(".cs")
            .Where(file => !ConstitutionSource.Relative(file).StartsWith("Constitution.Tests/", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .Where(file => !ConstitutionSource.Relative(file).StartsWith("Constitution.Domain/Documents/Digest/", StringComparison.Ordinal))
            .Where(file => Mention.IsMatch(File.ReadAllText(file)))
            .Select(ConstitutionSource.Relative)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void TheDigestFolderDoesHoldTheService()
    {
        var home = ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Domain/Documents/Digest/", StringComparison.Ordinal))
            .Any(file => File.ReadAllText(file).Contains("static class Correlation", StringComparison.Ordinal));
        Assert.True(home, "The correlation service is expected under Constitution.Domain/Documents/Digest/.");
    }
}
