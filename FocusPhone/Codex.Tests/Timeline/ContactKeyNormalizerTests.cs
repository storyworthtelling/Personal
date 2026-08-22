using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Timeline;

public class ContactKeyNormalizerTests
{
    [Theory]
    [InlineData("+1 (555) 123-4567", "5551234567")]
    [InlineData("555-123-4567", "5551234567")]
    [InlineData("15551234567", "5551234567")]
    [InlineData("555.123.4567", "5551234567")]
    public void NormalizePhone_StripsFormattingAndCountryCode(string input, string sameAs)
    {
        Assert.Equal(
            ContactKeyNormalizer.NormalizePhone(sameAs),
            ContactKeyNormalizer.NormalizePhone(input));
    }

    [Fact]
    public void NormalizePhone_DifferentNumbers_ProduceDifferentKeys()
    {
        Assert.NotEqual(
            ContactKeyNormalizer.NormalizePhone("5551234567"),
            ContactKeyNormalizer.NormalizePhone("5557654321"));
    }

    [Fact]
    public void NormalizePhone_ShortCode_IsPreserved()
    {
        Assert.Equal("88202", ContactKeyNormalizer.NormalizePhone("88202"));
    }

    [Theory]
    [InlineData("Alice@Example.COM", "alice@example.com")]
    [InlineData("  alice@example.com  ", "alice@example.com")]
    public void NormalizeEmail_LowercasesAndTrims(string input, string expected)
    {
        Assert.Equal(expected, ContactKeyNormalizer.NormalizeEmail(input));
    }

    [Theory]
    [InlineData("+1 (555) 123-4567", "5551234567")]
    [InlineData("15551234567", "5551234567")]
    public void NormalizeAny_Phoneish_UsesLastTenDigits(string input, string expected)
    {
        Assert.Equal(expected, ContactKeyNormalizer.NormalizeAny(input));
    }

    [Fact]
    public void NormalizeAny_Email_Lowercases()
    {
        Assert.Equal("alice@example.com", ContactKeyNormalizer.NormalizeAny(" Alice@Example.COM "));
    }

    [Theory]
    [InlineData("Mom", "mom")]
    [InlineData("  Uncle Bob  ", "uncle bob")]
    public void NormalizeAny_BareName_TrimsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, ContactKeyNormalizer.NormalizeAny(input));
    }
}
