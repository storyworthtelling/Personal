using FocusPhone.Domain.Timeline;
using Xunit;

namespace FocusPhone.Tests.Timeline;

public class RecipientInputTests
{
    [Theory]
    [InlineData("(555) 123-4567", "5551234567")]
    [InlineData("555.123.4567", "5551234567")]
    [InlineData(" 555 123 4567 ", "5551234567")]
    public void TryCreatePhone_StripsFormatting(string raw, string expected)
    {
        Assert.True(RecipientInput.TryCreatePhone(raw, out var dialable));
        Assert.Equal(expected, dialable);
    }

    [Fact]
    public void TryCreatePhone_KeepsLeadingPlus()
    {
        Assert.True(RecipientInput.TryCreatePhone("+1 555-123-4567", out var dialable));
        Assert.Equal("+15551234567", dialable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("call mom")]
    [InlineData("+")]
    public void TryCreatePhone_NoDigits_ReturnsFalse(string raw)
    {
        Assert.False(RecipientInput.TryCreatePhone(raw, out _));
    }

    [Fact]
    public void TryCreateEmail_TrimsAndLowercases()
    {
        Assert.True(RecipientInput.TryCreateEmail("  Alice@Example.COM ", out var address));
        Assert.Equal("alice@example.com", address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("alice")]
    [InlineData("alice@")]
    [InlineData("@example.com")]
    [InlineData("alice@example")]
    [InlineData("alice example@example.com")]
    public void TryCreateEmail_MissingAtOrDomain_ReturnsFalse(string raw)
    {
        Assert.False(RecipientInput.TryCreateEmail(raw, out _));
    }
}
