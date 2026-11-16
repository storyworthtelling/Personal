using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class PlayRatingTests
{
    [Theory]
    [InlineData("Everyone", ContentRating.Everyone)]
    [InlineData("Everyone 10+", ContentRating.Everyone)]
    [InlineData("Teen", ContentRating.Teen)]
    [InlineData("Mature 17+", ContentRating.Mature)]
    [InlineData("mature 17+", ContentRating.Mature)]
    [InlineData("Mature 17+", ContentRating.Mature)]
    [InlineData("Adults only 18+", ContentRating.AdultsOnly)]
    [InlineData("Unrated", ContentRating.Unknown)]
    [InlineData("Rated for 3+", ContentRating.Unknown)]
    [InlineData("", ContentRating.Unknown)]
    [InlineData(null, ContentRating.Unknown)]
    public void FromLabel_ReadsTheUsLabels(string? label, ContentRating expected)
    {
        Assert.Equal(expected, PlayRating.FromLabel(label));
    }

    [Theory]
    [InlineData(ContentRating.Mature, true)]
    [InlineData(ContentRating.AdultsOnly, true)]
    [InlineData(ContentRating.Teen, false)]
    [InlineData(ContentRating.Everyone, false)]
    [InlineData(ContentRating.Unknown, false)]
    public void IsMature_IsSeventeenAndUp(ContentRating rating, bool expected)
    {
        Assert.Equal(expected, PlayRating.IsMature(rating));
    }

    [Fact]
    public void ReadListing_TakesTheStructuredDataRating()
    {
        const string html = """
            <html><head><script type="application/ld+json" nonce="x">{"@context":"https://schema.org",
            "@type":"SoftwareApplication","name":"Example Forum","contentRating":"Mature 17+",
            "applicationCategory":"SOCIAL"}</script></head><body></body></html>
            """;

        Assert.Equal(ContentRating.Mature, PlayRating.ReadListing(html));
    }

    [Fact]
    public void ReadListing_FallsBackToTheVisibleLabel()
    {
        const string html = """
            <div class="xg1aie"><span itemprop="contentRating"><span>Adults only 18+</span></span></div>
            """;

        Assert.Equal(ContentRating.AdultsOnly, PlayRating.ReadListing(html));
    }

    [Fact]
    public void ReadListing_DecodesEntities()
    {
        const string html = """<span itemprop="contentRating"><span>Everyone 10&#43;</span></span>""";

        Assert.Equal(ContentRating.Everyone, PlayRating.ReadListing(html));
    }

    [Fact]
    public void ReadListing_WithNoRatingOnThePage_IsUnknown()
    {
        Assert.Equal(ContentRating.Unknown, PlayRating.ReadListing("<html><body>Something else</body></html>"));
    }
}
