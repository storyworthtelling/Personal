using Pulp.Domain.Children;
using Pulp.Domain.Ratings;

namespace Pulp.Tests.Children;

/// <summary>D127 — one ceiling, an age band, read against both MPA film and US TV ratings.</summary>
public class AgeBandTests
{
    [Theory]
    [InlineData(AgeBand.UnderSeven, "G", true)]
    [InlineData(AgeBand.UnderSeven, "TV-Y", true)]
    [InlineData(AgeBand.UnderSeven, "TV-G", true)]
    [InlineData(AgeBand.UnderSeven, "TV-Y7", false)]
    [InlineData(AgeBand.UnderSeven, "PG", false)]
    [InlineData(AgeBand.SevenToNine, "TV-Y7", true)]
    [InlineData(AgeBand.SevenToNine, "TV-Y7-FV", true)]
    [InlineData(AgeBand.SevenToNine, "PG", false)]
    [InlineData(AgeBand.SevenToNine, "TV-PG", false)]
    [InlineData(AgeBand.TenToTwelve, "PG", true)]
    [InlineData(AgeBand.TenToTwelve, "TV-PG", true)]
    [InlineData(AgeBand.TenToTwelve, "PG-13", false)]
    [InlineData(AgeBand.TenToTwelve, "TV-14", false)]
    [InlineData(AgeBand.ThirteenToSixteen, "PG-13", true)]
    [InlineData(AgeBand.ThirteenToSixteen, "TV-14", true)]
    [InlineData(AgeBand.ThirteenToSixteen, "R", false)]
    [InlineData(AgeBand.ThirteenToSixteen, "TV-MA", false)]
    [InlineData(AgeBand.Seventeen, "R", true)]
    [InlineData(AgeBand.Seventeen, "TV-MA", true)]
    public void EachBandAdmitsItsRatingsInBothSystems(AgeBand band, string rating, bool allowed) =>
        Assert.Equal(allowed, AgeBands.Allows(band, ContentRating.Parse(rating)));

    [Theory]
    [InlineData("NC-17")]
    [InlineData("X")]
    public void NoChildBandEverAdmitsAnAdultsOnlyRating(string rating) =>
        Assert.All(Enum.GetValues<AgeBand>(), band => Assert.False(AgeBands.Allows(band, ContentRating.Parse(rating))));

    [Fact]
    public void NoBandAdmitsAnUnratedTitle() =>
        Assert.All(Enum.GetValues<AgeBand>(), band => Assert.False(AgeBands.Allows(band, ContentRating.Unrated)));

    [Fact]
    public void EveryRatingAnOlderBandRefusesAYoungerBandRefusesToo()
    {
        var bands = Enum.GetValues<AgeBand>().OrderBy(band => band).ToList();
        var ratings = new[] { "G", "TV-Y", "TV-Y7", "TV-G", "PG", "TV-PG", "PG-13", "TV-14", "R", "TV-MA", "NC-17" };
        Assert.Equal(5, bands.Count);

        for (var older = 1; older < bands.Count; older++)
        {
            foreach (var rating in ratings)
            {
                if (AgeBands.Allows(bands[older - 1], ContentRating.Parse(rating)))
                {
                    Assert.True(AgeBands.Allows(bands[older], ContentRating.Parse(rating)), $"{bands[older]} refuses {rating}");
                }
            }
        }
    }

    [Theory]
    [InlineData(AgeBand.UnderSeven, 0, 6)]
    [InlineData(AgeBand.SevenToNine, 7, 9)]
    [InlineData(AgeBand.TenToTwelve, 10, 12)]
    [InlineData(AgeBand.ThirteenToSixteen, 13, 16)]
    [InlineData(AgeBand.Seventeen, 17, 17)]
    public void ABandNamesItsAgesForTheVoicesReadingLevel(AgeBand band, int youngest, int oldest)
    {
        Assert.Equal(youngest, AgeBands.Youngest(band));
        Assert.Equal(oldest, AgeBands.Oldest(band));
    }
}
