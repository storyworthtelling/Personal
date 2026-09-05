using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class PlaylistImportTests
{
    private static Track T(string id, string title, string artist) =>
        new(id, title, artist, "Album", artist, 1, 1, "Rock", TimeSpan.FromMinutes(3));

    private static readonly IReadOnlyList<Track> Library =
    [
        T("1", "Dare You To Move", "Switchfoot"),
        T("2", "Meant to Live", "Switchfoot"),
        T("3", "Oh! Gravity (Acoustic)", "Switchfoot"),
        T("4", "Build My Life", "Pat Barrett"),
    ];

    private static PlaylistImportResult Resolve(params ImportedTrack[] wanted) =>
        PlaylistImport.Resolve(wanted, Library);

    [Fact]
    public void Resolve_MatchesOnTitleAndArtist()
    {
        var result = Resolve(new ImportedTrack("Dare You To Move", "Switchfoot"));

        Assert.Equal(["1"], result.Matched.Select(track => track.Id));
        Assert.Empty(result.Missing);
    }

    // A setlist arrives in the order it will be played, and that order is the point.
    [Fact]
    public void Resolve_KeepsTheOrderOfTheSharedList()
    {
        var result = Resolve(
            new ImportedTrack("Build My Life", "Pat Barrett"),
            new ImportedTrack("Meant to Live", "Switchfoot"));

        Assert.Equal(["4", "2"], result.Matched.Select(track => track.Id));
    }

    // The noise a shared playlist carries. None of it changes which song is meant.
    [Theory]
    [InlineData("Dare You To Move - 2011 Remaster")]
    [InlineData("Dare You To Move - Remastered 2011")]
    [InlineData("Dare You To Move (Radio Edit)")]
    [InlineData("Dare You To Move [Live]")]
    [InlineData("Dare You To Move (feat. Someone Else)")]
    [InlineData("Dare You To Move - Single Version")]
    public void Resolve_SeesPastEditionSuffixes(string title)
    {
        var result = Resolve(new ImportedTrack(title, "Switchfoot"));

        Assert.Equal(["1"], result.Matched.Select(track => track.Id));
    }

    // And the same in reverse: the library file is the variant, the shared list is plain.
    [Fact]
    public void Resolve_MatchesWhenTheLibraryFileIsTheVariant()
    {
        var result = Resolve(new ImportedTrack("Oh! Gravity", "Switchfoot"));

        Assert.Equal(["3"], result.Matched.Select(track => track.Id));
    }

    // Shared lists credit every performer; the tag usually credits one.
    [Fact]
    public void Resolve_MatchesWhenTheSharedListCreditsExtraArtists()
    {
        var result = Resolve(new ImportedTrack("Build My Life", "Pat Barrett, Chris Tomlin"));

        Assert.Equal(["4"], result.Matched.Select(track => track.Id));
    }

    [Fact]
    public void Resolve_IgnoresCaseAndAccents()
    {
        var result = Resolve(new ImportedTrack("MEANT TO LIVE", "switchfoot"));

        Assert.Equal(["2"], result.Matched.Select(track => track.Id));
    }

    // A partial match is the normal case, so what is missing has to come back by
    // name — dropping songs silently is the failure mode this exists to avoid.
    [Fact]
    public void Resolve_ReportsWhatTheLibraryDoesNotHave()
    {
        var absent = new ImportedTrack("Some Song We Don't Own", "A Band");
        var result = Resolve(new ImportedTrack("Meant to Live", "Switchfoot"), absent);

        Assert.Equal(["2"], result.Matched.Select(track => track.Id));
        Assert.Equal([absent], result.Missing);
    }

    // The same title by a different artist is a different song.
    [Fact]
    public void Resolve_DoesNotMatchTheRightTitleByTheWrongArtist()
    {
        var result = Resolve(new ImportedTrack("Dare You To Move", "Vitamin String Quartet"));

        Assert.Empty(result.Matched);
        Assert.Single(result.Missing);
    }

    [Fact]
    public void Resolve_NeverReturnsTheSameTrackTwice()
    {
        var result = Resolve(
            new ImportedTrack("Meant to Live", "Switchfoot"),
            new ImportedTrack("Meant to Live - Remastered", "Switchfoot"));

        Assert.Single(result.Matched);
        Assert.Single(result.Missing);
    }

    [Fact]
    public void Resolve_PrefersAnExactTitleOverASimplifiedOne()
    {
        IReadOnlyList<Track> both = [T("a", "Song (Acoustic)", "Band"), T("b", "Song", "Band")];

        var result = PlaylistImport.Resolve([new ImportedTrack("Song", "Band")], both);

        Assert.Equal(["b"], result.Matched.Select(track => track.Id));
    }

    [Fact]
    public void Resolve_SurvivesAnEmptyList()
    {
        var result = PlaylistImport.Resolve([], Library);

        Assert.Empty(result.Matched);
        Assert.Empty(result.Missing);
    }
}
