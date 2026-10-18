using Constitution.Domain.Health;

namespace Constitution.Tests.Health;

/// <summary>
/// The sentence the permissions-rationale page prints (C-7.1.1). It lives in Domain as a plain
/// constant so a test can read it; the page is a thin painter over it.
/// </summary>
public class HealthRationaleTests
{
    private static readonly string[] Texts =
    [
        HealthRationale.Title, HealthRationale.Sentence, HealthRationale.BackgroundSentence,
    ];

    [Fact]
    public void SentenceSaysItOnlyReads()
    {
        Assert.Contains("only reads", HealthRationale.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void SentenceNeverPromisesToWrite()
    {
        Assert.Contains("never writes", HealthRationale.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void NoTextCarriesADigit()
    {
        // A count or a date typed into a sentence is a number that can drift from the permissions.
        foreach (var text in Texts)
        {
            Assert.False(text.Any(char.IsDigit), "A rationale text carries a digit: " + text);
        }
    }

    [Fact]
    public void NoTextIsBlank()
    {
        Assert.All(Texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
    }

    [Fact]
    public void SentenceNamesEverySignalItReads()
    {
        foreach (var word in new[] { "weight", "heart rate", "steps", "sleep", "exercise" })
        {
            Assert.Contains(word, HealthRationale.Sentence, StringComparison.OrdinalIgnoreCase);
        }
    }
}
