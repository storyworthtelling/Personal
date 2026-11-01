using System.Text.RegularExpressions;

namespace LogLoopReader.Tests;

// The reader's Google OAuth client cannot be published without a home page and a privacy policy, so both
// are served by GitHub Pages from docs/. The repository is public: the pages say what the reader does and
// carry no address, number or account of anyone's.
public partial class ReaderPagesTests
{
    private static string Page(string name) =>
        File.ReadAllText(Path.Combine(Support.RepoRoot(), "docs", name));

    [Fact]
    public void TheHomePageNamesTheToolAndLinksThePrivacyPolicy()
    {
        var home = Page("index.html");

        Assert.Contains("Log loop reader", home);
        Assert.Contains("href=\"privacy.html\"", home);
        Assert.Contains("drive.readonly", home);
    }

    [Fact]
    public void ThePrivacyPolicySaysWhatIsReadKeptAndShared()
    {
        var privacy = Page("privacy.html");

        Assert.Contains("Privacy policy", privacy);
        Assert.Contains("storyworthtelling-logs", privacy);
        Assert.Contains("drive.readonly", privacy);
        Assert.Contains("not sold", privacy);
        Assert.Contains("third-party access", privacy);
        Assert.Contains("href=\"index.html\"", privacy);
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("privacy.html")]
    public void NeitherPageCarriesAnAddressOrANumber(string name)
    {
        var page = Page(name);

        Assert.DoesNotMatch(EmailAddress(), page);
        Assert.DoesNotMatch(PhoneNumber(), page);
        Assert.DoesNotContain("<script", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PagesServesTheFilesAsTheyAre()
    {
        Assert.True(File.Exists(Path.Combine(Support.RepoRoot(), "docs", ".nojekyll")));
    }

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"\d{3}[-. )]\d{3}[-. ]\d{4}")]
    private static partial Regex PhoneNumber();
}
