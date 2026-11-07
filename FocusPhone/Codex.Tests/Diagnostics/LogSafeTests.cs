using Codex.Domain.Diagnostics;
using Xunit;

namespace Codex.Tests.Diagnostics;

// Privacy report §4: Post wrote a full phone number into the durable log when a block failed, and the
// full URL of a link from an email (which can carry a reset token or the recipient's address) when it
// could not be opened. The log loop uploads that log. LogSafe keeps what a diagnosis needs and no more.
public class LogSafeTests
{
    [Theory]
    [InlineData("+1 (555) 010-0142", "…42")]
    [InlineData("5550100199", "…99")]
    [InlineData("7", "a number")]
    [InlineData("", "a number")]
    [InlineData(null, "a number")]
    public void ANumberIsItsLastTwoDigits(string? number, string expected)
    {
        Assert.Equal(expected, LogSafe.Number(number));
    }

    [Theory]
    [InlineData("https://shop.example/reset?token=abc123&email=sam%40example.com", "https link to shop.example")]
    [InlineData("http://news.example.org/a/b/c", "http link to news.example.org")]
    [InlineData("mailto:sam@example.com", "mailto link")]
    [InlineData("tel:5550100142", "tel link")]
    [InlineData("not a url", "an unreadable link")]
    [InlineData(null, "an unreadable link")]
    public void ALinkIsItsSchemeAndHostOnly(string? url, string expected)
    {
        Assert.Equal(expected, LogSafe.Link(url));
    }
}
