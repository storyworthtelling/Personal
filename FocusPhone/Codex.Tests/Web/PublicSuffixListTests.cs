using Codex.Domain.Web;
using Xunit;

namespace Codex.Tests.Web;

public class PublicSuffixListTests
{
    private static readonly PublicSuffixList SampleList = PublicSuffixList.Parse("""
        // comment line
        com
        co.uk
        uk
        github.io

        *.ck
        !www.ck
        """);

    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("a.b.example.com", "example.com")]
    [InlineData("example.co.uk", "example.co.uk")]
    [InlineData("deep.sub.example.co.uk", "example.co.uk")]
    [InlineData("user.github.io", "user.github.io")]
    [InlineData("page.user.github.io", "user.github.io")]
    public void GetRegistrableDomain_NormalRules(string host, string expected)
    {
        Assert.Equal(expected, SampleList.GetRegistrableDomain(host));
    }

    [Fact]
    public void GetRegistrableDomain_WildcardRule_TreatsLabelUnderWildcardAsSuffix()
    {
        Assert.Equal("foo.bar.ck", SampleList.GetRegistrableDomain("foo.bar.ck"));
        Assert.Equal("foo.bar.ck", SampleList.GetRegistrableDomain("x.foo.bar.ck"));
    }

    [Fact]
    public void GetRegistrableDomain_ExceptionRule_OverridesWildcard()
    {
        Assert.Equal("www.ck", SampleList.GetRegistrableDomain("www.ck"));
        Assert.Equal("www.ck", SampleList.GetRegistrableDomain("sub.www.ck"));
    }

    [Fact]
    public void GetRegistrableDomain_UnknownTld_FallsBackToLastTwoLabels()
    {
        Assert.Equal("host.internal", SampleList.GetRegistrableDomain("deep.host.internal"));
    }

    [Fact]
    public void GetRegistrableDomain_HostEqualToSuffix_ReturnsNull()
    {
        Assert.Null(SampleList.GetRegistrableDomain("co.uk"));
        Assert.Null(SampleList.GetRegistrableDomain("com"));
    }

    [Fact]
    public void GetRegistrableDomain_IsCaseInsensitive()
    {
        Assert.Equal("example.com", SampleList.GetRegistrableDomain("WWW.Example.COM"));
    }

    [Fact]
    public void Embedded_LoadsRealListWithMultiLabelSuffixes()
    {
        var psl = PublicSuffixList.LoadEmbedded();

        Assert.Equal("example.co.uk", psl.GetRegistrableDomain("a.b.example.co.uk"));
        Assert.Equal("user.github.io", psl.GetRegistrableDomain("user.github.io"));
        Assert.Equal("example.com", psl.GetRegistrableDomain("www.example.com"));
    }
}
