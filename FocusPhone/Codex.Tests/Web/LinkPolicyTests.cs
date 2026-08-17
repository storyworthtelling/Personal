using Codex.Domain.Web;
using Xunit;

namespace Codex.Tests.Web;

public class LinkPolicyTests
{
    private static readonly LinkPolicy Policy = new(PublicSuffixList.Parse("""
        com
        co.uk
        io
        github.io
        """));

    private static Uri U(string url) => new(url);

    [Fact]
    public void SameHost_IsAllowed()
    {
        Assert.True(Policy.IsNavigationAllowed(U("https://example.com/a"), U("https://example.com/b")));
    }

    [Fact]
    public void Subdomain_OfSameRegistrableDomain_IsAllowed()
    {
        Assert.True(Policy.IsNavigationAllowed(U("https://www.example.com/"), U("https://shop.example.com/cart")));
    }

    [Fact]
    public void DifferentRegistrableDomain_IsBlocked()
    {
        Assert.False(Policy.IsNavigationAllowed(U("https://example.com/"), U("https://evil.com/")));
    }

    [Fact]
    public void SharedPublicSuffix_IsNotSharedDomain()
    {
        Assert.False(Policy.IsNavigationAllowed(
            U("https://a.example.co.uk/"), U("https://other.co.uk/")));
        Assert.False(Policy.IsNavigationAllowed(
            U("https://alice.github.io/"), U("https://bob.github.io/")));
    }

    [Fact]
    public void SchemeChange_BetweenHttpAndHttps_IsAllowed()
    {
        Assert.True(Policy.IsNavigationAllowed(U("http://example.com/"), U("https://example.com/")));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("intent://scan/#Intent;scheme=zxing;end")]
    [InlineData("market://details?id=com.example")]
    [InlineData("file:///etc/passwd")]
    public void NonHttpTargetScheme_IsBlocked(string target)
    {
        Assert.False(Policy.IsNavigationAllowed(U("https://example.com/"), U(target)));
    }

    [Fact]
    public void TargetWithoutHost_IsBlocked()
    {
        Assert.False(Policy.IsNavigationAllowed(U("https://example.com/"), U("about:blank")));
    }

    [Fact]
    public void OriginHostEqualToPublicSuffix_BlocksEverything()
    {
        Assert.False(Policy.IsNavigationAllowed(U("https://co.uk/"), U("https://co.uk/page")));
    }
}
