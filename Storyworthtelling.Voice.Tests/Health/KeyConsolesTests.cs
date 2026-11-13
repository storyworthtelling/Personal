using Storyworthtelling.Voice.Health;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Health;

public class KeyConsolesTests
{
    [Theory]
    [InlineData("claude", "https://platform.claude.com/settings/keys")]
    [InlineData("gemini", "https://aistudio.google.com/app/apikey")]
    [InlineData("maps", "https://console.cloud.google.com/google/maps-apis/credentials")]
    [InlineData("youtube", "https://console.cloud.google.com/apis/credentials")]
    [InlineData("google-oauth-client", "https://console.cloud.google.com/apis/credentials")]
    [InlineData("nyt-api-key", "https://developer.nytimes.com/my-apps")]
    [InlineData("plaid", "https://dashboard.plaid.com/developers/keys")]
    [InlineData("streaming-availability", "https://developers.movieofthenight.com/")]
    [InlineData("omdb", "https://www.omdbapi.com/apikey.aspx")]
    [InlineData("github-update-token", "https://github.com/settings/personal-access-tokens/new")]
    [InlineData("gmail-app-password", "https://myaccount.google.com/apppasswords")]
    [InlineData("tidal", "https://developer.tidal.com/dashboard")]
    [InlineData("remarkable", "https://my.remarkable.com/device/mobile/connect")]
    public void EachCredentialOpensItsOwnConsole(string credentialId, string address)
    {
        var console = KeyConsoles.For(credentialId);

        Assert.NotNull(console);
        Assert.Equal(address, console.Address.AbsoluteUri);
        Assert.Equal(credentialId, console.CredentialId);
    }

    [Fact]
    public void EveryConsoleIsHttpsWithALabel()
    {
        Assert.NotEmpty(KeyConsoles.All);
        foreach (var console in KeyConsoles.All)
        {
            Assert.Equal(Uri.UriSchemeHttps, console.Address.Scheme);
            Assert.False(string.IsNullOrWhiteSpace(console.Label));
        }
    }

    [Fact]
    public void NoCredentialIsListedTwice()
    {
        var ids = KeyConsoles.All.Select(console => console.CredentialId).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("nyt-cookie")]
    [InlineData("sleep-number")]
    [InlineData("")]
    public void ACredentialWithNoConsoleHasNoLink(string credentialId)
    {
        Assert.Null(KeyConsoles.For(credentialId));
    }

    [Fact]
    public void TheSharedIdsResolve()
    {
        Assert.NotNull(KeyConsoles.For(SharedCredentialIds.Claude));
        Assert.NotNull(KeyConsoles.For(SharedCredentialIds.Gemini));
        Assert.NotNull(KeyConsoles.For(SharedCredentialIds.Remarkable));
        Assert.NotNull(KeyConsoles.For(SharedCredentialIds.GitHubUpdateToken));
        Assert.NotNull(KeyConsoles.For(KeyConsoles.GoogleOAuthClient));
    }
}
