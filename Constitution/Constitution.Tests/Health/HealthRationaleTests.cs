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
        HealthRationale.Title, HealthRationale.Sentence, HealthRationale.BackgroundSentence, HealthRationale.OvernightLine,
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

    [Fact]
    public void TheSentenceNamesBodyFatAndNotLeanMass()
    {
        Assert.Contains("body fat", HealthRationale.Sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("lean", HealthRationale.Sentence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OvernightReadsAreAskedForAndNotPromised()
    {
        // Review S10: the permission was declared and never requested, so "where Android allows it" promised what never happened.
        Assert.Equal(
            "Allow overnight reads in Health Connect so the morning check is current; otherwise values refresh when you open the app.",
            HealthRationale.OvernightLine);
        Assert.Equal(
            "Constitution reads overnight only if you allow it in Health Connect, so the morning check is current. "
            + "Otherwise it reads whenever you open the app.",
            HealthRationale.BackgroundSentence);
        Assert.DoesNotContain("Where Android allows", HealthRationale.BackgroundSentence, StringComparison.Ordinal);
        Assert.DoesNotContain("decline", HealthRationale.BackgroundSentence, StringComparison.OrdinalIgnoreCase);
    }
}
