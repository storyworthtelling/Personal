using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Title;
using static Pulp.Tests.Title.TitleFixtures;

namespace Pulp.Tests.Title;

/// <summary>
/// The title page's words (mockup 02, D36, D158, D216, D272). Invented titles and services only.
///
/// <para>The bugs these reintroduce: a show line that says "1 seasons" or drops the episode count
/// (D36), a film page that shows an episode count instead of its runtime (D272), a Play button that
/// does not say what it will play (D158), a where line that hides the ads (D29) or the picture
/// (D28), and an "also on" line that forgets to say the other home has ads.</para>
/// </summary>
public class TitlePageTextTests
{
    [Fact]
    public void AShowReadsGenreThenSeasonsThenEpisodesAsDrawn()
    {
        Assert.Equal("Mystery · 2 seasons · 14 episodes", TitlePageText.KindLine(Show()));
    }

    [Fact]
    public void OneSeasonAndOneEpisodeAreSingular()
    {
        var show = Show() with { SeasonCount = 1, EpisodeCount = 1, Seasons = [Season(1, 1, Now.AddDays(-9))] };

        Assert.Equal("Mystery · 1 season · 1 episode", TitlePageText.KindLine(show));
    }

    [Fact]
    public void AShowWithoutCountsCountsItsListedSeasons()
    {
        var show = Show() with { SeasonCount = null, EpisodeCount = null };

        Assert.Equal("Mystery · 2 seasons · 14 episodes", TitlePageText.KindLine(show));
    }

    [Fact]
    public void AFilmReadsGenreThenRuntimeNeverAnEpisodeCount()
    {
        Assert.Equal("Thriller · 1h 52m", TitlePageText.KindLine(Film()));
    }

    [Fact]
    public void WhatIsNotKnownIsLeftOutRatherThanGuessed()
    {
        Assert.Equal("1h 52m", TitlePageText.KindLine(Film() with { Genres = [] }));
        Assert.Equal("Thriller", TitlePageText.KindLine(Film() with { RuntimeMinutes = null }));
    }

    [Theory]
    [InlineData(112, "1h 52m")]
    [InlineData(45, "45m")]
    [InlineData(120, "2h")]
    [InlineData(61, "1h 1m")]
    public void RuntimeIsHoursAndMinutes(int minutes, string expected)
    {
        Assert.Equal(expected, TitlePageText.Runtime(minutes));
    }

    [Fact]
    public void ARuntimeOfNothingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TitlePageText.Runtime(0));
    }

    [Fact]
    public void PlayNamesTheEpisodeItWillPlay()
    {
        Assert.Equal("Play S1 · E3", TitlePageText.PlayLabel(Show(), E(1, 3)));
    }

    [Fact]
    public void PlayOnAFilmNamesTheFilm()
    {
        Assert.Equal("Play Larkspur", TitlePageText.PlayLabel(Film(), null));
    }

    [Fact]
    public void AShowWithNothingToPlayIsPlainPlay()
    {
        Assert.Equal("Play", TitlePageText.PlayLabel(Show(), null));
    }

    [Fact]
    public void TheBestHomeNamesTheServiceAndThePicture()
    {
        Assert.Equal("On Northstar+ · 4K", TitlePageText.WhereLine(new Home(Northstar, VideoQuality.Uhd, WithAds: false), "Northstar+"));
        Assert.Equal("On Northstar+ · HD, with ads", TitlePageText.WhereLine(new Home(Northstar, VideoQuality.Hd, WithAds: true), "Northstar+"));
        Assert.Equal("On Northstar+ · SD", TitlePageText.WhereLine(new Home(Northstar, VideoQuality.Sd, WithAds: false), "Northstar+"));
    }

    [Fact]
    public void TheOtherHomeSaysWhenItHasAds()
    {
        Assert.Equal("Also on Lanternbox, with ads", TitlePageText.AlsoLine(new Home(Lanternbox, VideoQuality.Hd, WithAds: true), "Lanternbox"));
        Assert.Equal("Also on Lanternbox", TitlePageText.AlsoLine(new Home(Lanternbox, VideoQuality.Hd, WithAds: false), "Lanternbox"));
        Assert.Equal("open there instead", TitlePageText.OpenThereInstead);
    }

    [Theory]
    [InlineData(6, 2, "6 episodes · 2 watched")]
    [InlineData(8, 0, "8 episodes")]
    [InlineData(1, 1, "1 episode · 1 watched")]
    public void ASeasonHeaderCountsItsEpisodesAndWhatIsWatched(int episodes, int watched, string expected)
    {
        Assert.Equal(expected, TitlePageText.SeasonSummary(episodes, watched));
    }

    [Fact]
    public void ASeasonIsHeadedByItsNumberNotItsSourceName()
    {
        Assert.Equal("Season 2", TitlePageText.SeasonHeading(new CatalogueSeason(2, "The Drowned Chapter", [])));
    }
}
