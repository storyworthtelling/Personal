using Codex.Infrastructure.Email;
using Xunit;

namespace Codex.Tests.Email;

public class GmailCredentialsTests
{
    // Google shows the 16-character app password as four space-separated groups
    // ("abcd efgh ijkl mnop"). The spaces are presentational only; Gmail rejects
    // them and answers "Application-specific password required", the very error
    // seen in the field. Normalising strips every whitespace character so a
    // pasted-verbatim password authenticates.

    [Theory]
    [InlineData("abcd efgh ijkl mnop", "abcdefghijklmnop")]
    [InlineData("  abcd efgh ijkl mnop  ", "abcdefghijklmnop")]
    [InlineData("abcd\tefgh\r\nijkl mnop", "abcdefghijklmnop")]
    [InlineData("abcdefghijklmnop", "abcdefghijklmnop")]
    public void NormalizeAppPassword_StripsAllWhitespace(string input, string expected)
    {
        Assert.Equal(expected, GmailCredentials.NormalizeAppPassword(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeAppPassword_BlankBecomesEmpty(string? input)
    {
        Assert.Equal(string.Empty, GmailCredentials.NormalizeAppPassword(input));
    }

    [Theory]
    [InlineData("  owner@gmail.com  ", "owner@gmail.com")]
    [InlineData("Owner@Gmail.com", "owner@gmail.com")]
    [InlineData("owner@gmail.com", "owner@gmail.com")]
    public void NormalizeAddress_TrimsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, GmailCredentials.NormalizeAddress(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void NormalizeAddress_BlankBecomesEmpty(string? input)
    {
        Assert.Equal(string.Empty, GmailCredentials.NormalizeAddress(input));
    }

    [Fact]
    public void GmailClient_NormalizesCredentials_SoSpacedAppPasswordAuthenticates()
    {
        // The client is the transport seam the worker uses; it must not pass a
        // space-laden paste straight through to IMAP/SMTP.
        var client = new GmailClient("  Owner@Gmail.com ", "abcd efgh ijkl mnop");
        Assert.Equal("owner@gmail.com", client.AccountAddress);
        Assert.Equal("abcdefghijklmnop", client.AppPassword);
    }
}
