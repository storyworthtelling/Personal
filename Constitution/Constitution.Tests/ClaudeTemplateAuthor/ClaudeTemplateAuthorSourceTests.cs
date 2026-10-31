using System.Text.RegularExpressions;

namespace Constitution.Tests.ClaudeTemplateAuthor;

/// <summary>What the author's files must not hold and where the key may be read, by content: the head cannot be loaded here.</summary>
public partial class ClaudeTemplateAuthorSourceTests
{
    private const string AuthorPath = "Constitution.Infrastructure/Meals/ClaudeTemplateAuthor.cs";
    private const string ServicesPath = "Constitution.Android/Services/TemplateAuthorServices.cs";
    private const string WeekPath = "Constitution.Android/MealWeek/MealWeekServices.cs";

    private static string Read(string relative) => File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative));

    [GeneratedRegex(@"claude-[a-z0-9]|sonnet|haiku|opus", RegexOptions.IgnoreCase)]
    private static partial Regex AModelName();

    [GeneratedRegex(@"#[0-9A-Fa-f]{6,8}\b|0x[0-9A-Fa-f]{8}\b|\bTextSize\b|\bFontSize\b|\bSetTextSize\b")]
    private static partial Regex AColourOrTypeSize();

    [GeneratedRegex(@"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\)\s*)?\{\s*(//[^\n]*\s*)?\}")]
    private static partial Regex AnEmptyCatch();

    [GeneratedRegex("\"(?:[^\"\\\\]|\\\\.)*\"|//[^\n]*")]
    private static partial Regex StringsAndComments();

    [GeneratedRegex(@"\b_\w|\w_\w|\w_\b")]
    private static partial Regex AnUnderscore();

    private static IEnumerable<string> Files() => [Read(AuthorPath), Read(ServicesPath)];

    [Fact]
    public void NoModelIdentifierIsNamedInTheNewFiles()
    {
        foreach (var text in Files())
        {
            Assert.Empty(AModelName().Matches(text));
        }
    }

    [Fact]
    public void NoColourOrTypeSizeIsNamedInTheNewFiles()
    {
        foreach (var text in Files())
        {
            Assert.Empty(AColourOrTypeSize().Matches(text));
        }
    }

    [Fact]
    public void NoCatchIsEmpty()
    {
        foreach (var text in Files())
        {
            Assert.Empty(AnEmptyCatch().Matches(text));
        }
    }

    [Fact]
    public void NoIdentifierHoldsAnUnderscore()
    {
        foreach (var text in Files())
        {
            Assert.Empty(AnUnderscore().Matches(StringsAndComments().Replace(text, string.Empty)));
        }
    }

    [Fact]
    public void TheKeyIsReadOnlyInTheServices()
    {
        Assert.DoesNotContain("ClaudeApiKey", Read(AuthorPath), StringComparison.Ordinal);
        Assert.DoesNotContain("ConstitutionConnections", Read(AuthorPath), StringComparison.Ordinal);
        Assert.DoesNotContain("ClaudeApiKey", Read(WeekPath), StringComparison.Ordinal);
        Assert.Contains("ClaudeApiKey", Read(ServicesPath), StringComparison.Ordinal);
    }

    [Fact]
    public void TheKeyIsNeverLoggedOrBuiltIntoText()
    {
        var author = Read(AuthorPath);
        Assert.DoesNotMatch(@"(Console|Log|Debug|Trace)\w*\.\w+\([^;]*ApiKey", author);
        Assert.DoesNotMatch(@"\$""[^""]*\{[^}]*ApiKey", author);
    }

    [Fact]
    public void TheRunIsHandedTheClaudeAuthorAndNoLongerNone()
    {
        var week = Read(WeekPath);

        Assert.Contains("TemplateAuthorServices.AuthorOf(", week, StringComparison.Ordinal);
        Assert.DoesNotContain("new NoTemplateAuthor()", week, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsSeededIntoTheLibrary()
    {
        // No starter lines in the library: with no key, pages 2 and 3 stay off.
        var author = Read(AuthorPath);
        Assert.DoesNotContain("new MealTemplate(", author, StringComparison.Ordinal);
    }
}
