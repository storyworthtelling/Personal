using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Friday;
using Pulp.Domain.Library;
using static Pulp.Tests.Discover.DiscoverFixtures;

namespace Pulp.Tests.Discover;

/// <summary>
/// The model-made playlist (D104, D272): from a prompt, from a seed title ("more like this") or from
/// an existing playlist, resolved against real titles with anything the catalogue cannot find dropped.
/// </summary>
public class PlaylistPlanTests
{
    private static DiscoverContext AdultContext() =>
        DiscoverContext.For(MoodViewer.Adult(Sam), Roster, HouseholdServices, TasteProfile.Empty);

    private static MoodCandidate C(string name, int? year = null) => new(name, year, null);

    [Fact]
    public void APromptSeedCarriesThePromptAndNoTitles()
    {
        var seed = PlaylistSeed.FromPrompt("  long sunday films  ");

        Assert.Equal(SeedKind.Prompt, seed.Kind);
        Assert.Equal("long sunday films", seed.Prompt);
        Assert.Empty(seed.Titles);
        Assert.Throws<ArgumentException>(() => PlaylistSeed.FromPrompt(" "));
    }

    [Fact]
    public void ATitleSeedCarriesWhatTheTitleIsAndNotItsId()
    {
        var seed = PlaylistSeed.FromTitle(Glasshouse);

        Assert.Equal(SeedKind.Title, seed.Kind);
        var only = Assert.Single(seed.Titles);
        Assert.Equal("The Glasshouse Year", only.Name);
        Assert.Equal(2019, only.Year);
        Assert.Equal(TitleKind.Film, only.Kind);
        Assert.Equal(["Drama"], only.Genres);
    }

    [Fact]
    public void APlaylistSeedReadsItsFilmsAndShowsAndSkipsWhatItCannotName()
    {
        var playlist = Playlist.Named("Rainy weekend")
            .Add(PlaylistEntry.ForTitle(Glasshouse.Id, TitleKind.Film))
            .Add(new PlaylistEntry(PlaylistEntryKind.Game, "game-example"))
            .Add(PlaylistEntry.ForTitle(new TitleId("title-gone"), TitleKind.Show))
            .Add(PlaylistEntry.ForTitle(Kettle.Id, TitleKind.Show));

        var seed = PlaylistSeed.FromPlaylist(playlist, entry => KnownTitles.FirstOrDefault(title => title.Id.Value == entry.Key));

        Assert.Equal(SeedKind.Playlist, seed.Kind);
        Assert.Equal(["The Glasshouse Year", "Kettle & Crumb"], seed.Titles.Select(title => title.Name));
        Assert.Equal("Rainy weekend", seed.Name);
    }

    [Fact]
    public void APlaylistSeedWithNothingNameableIsRefused()
    {
        var playlist = Playlist.Named("Games night").Add(new PlaylistEntry(PlaylistEntryKind.Game, "game-example"));

        Assert.Throws<ArgumentException>(() => PlaylistSeed.FromPlaylist(playlist, entry => null));
    }

    [Fact]
    public void TheMadePlaylistKeepsRealTitlesOnOurServicesAndDropsTheRest()
    {
        var query = PlaylistQuery.For(PlaylistSeed.FromPrompt("long sunday films"), AdultContext());
        var answer = MoodAnswer.Answered("Long Sundays", [C("Nightwater", 2017), C("Invented Picture", 2015), C("Far Shore", 2018), C("Kettle & Crumb")]);

        var made = PlaylistPlan.Resolve(query, answer, KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal("Long Sundays", made.Name);
        Assert.Equal([Nightwater.Id, Kettle.Id], made.Titles.Select(title => title.Id));
        Assert.Equal(1, made.Unresolved);
        Assert.Equal(1, made.OffServices);
    }

    [Fact]
    public void MoreLikeThisNeverHandsTheSeedBack()
    {
        var query = PlaylistQuery.For(PlaylistSeed.FromTitle(Glasshouse), AdultContext());
        var answer = MoodAnswer.Answered("Glass and light", [C("The Glasshouse Year", 2019), C("Nightwater", 2017)]);

        var made = PlaylistPlan.Resolve(query, answer, KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal([Nightwater.Id], made.Titles.Select(title => title.Id));
    }

    [Fact]
    public void AChildsMadePlaylistNeverHoldsATitleTheirFilterHides()
    {
        var context = DiscoverContext.For(MoodViewer.Child(WrenRules), Roster, HouseholdServices, TasteProfile.Empty);
        var query = PlaylistQuery.For(PlaylistSeed.FromPrompt("silly"), context);
        var answer = MoodAnswer.Answered("Silly", [C("Nightwater", 2017), C("Lantern Cove", 2020), C("Kettle & Crumb", 2021)]);

        var made = PlaylistPlan.Resolve(query, answer, KnownTitles, MoodViewer.Child(WrenRules), FranchisesOf);

        Assert.Equal([Kettle.Id], made.Titles.Select(title => title.Id));
        Assert.Equal(2, made.Hidden);
    }

    [Fact]
    public void ABlankModelNameFallsBackToTheSeed()
    {
        var fromTitle = PlaylistPlan.Resolve(PlaylistQuery.For(PlaylistSeed.FromTitle(Glasshouse), AdultContext()),
            MoodAnswer.Answered("  ", [C("Nightwater", 2017)]), KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);
        var fromPrompt = PlaylistPlan.Resolve(PlaylistQuery.For(PlaylistSeed.FromPrompt("long sunday films"), AdultContext()),
            MoodAnswer.NoIdea(MoodOutcome.Unavailable), KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal("The Glasshouse Year", fromTitle.Name);
        Assert.Equal("long sunday films", fromPrompt.Name);
        Assert.Empty(fromPrompt.Titles);
    }

    [Fact]
    public void TheMadePlaylistBecomesAnOrdinaryPlaylistInTheModelsOrder()
    {
        var query = PlaylistQuery.For(PlaylistSeed.FromPrompt("long sunday films"), AdultContext());
        var made = PlaylistPlan.Resolve(query, MoodAnswer.Answered("Long Sundays", [C("Kettle & Crumb"), C("Nightwater", 2017)]),
            KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        var playlist = made.ToPlaylist();

        Assert.Equal("Long Sundays", playlist.Name);
        Assert.Equal(
            [PlaylistEntry.ForTitle(Kettle.Id, TitleKind.Show), PlaylistEntry.ForTitle(Nightwater.Id, TitleKind.Film)],
            playlist.Entries);
    }

    [Fact]
    public void ThePlaylistIsCappedAtTheQuerysSize()
    {
        var query = PlaylistQuery.For(PlaylistSeed.FromPrompt("anything"), AdultContext(), maxTitles: 1);
        var made = PlaylistPlan.Resolve(query, MoodAnswer.Answered("Any", [C("Kettle & Crumb"), C("Nightwater", 2017)]),
            KnownTitles, MoodViewer.Adult(Sam), FranchisesOf);

        Assert.Equal([Kettle.Id], made.Titles.Select(title => title.Id));
    }
}
