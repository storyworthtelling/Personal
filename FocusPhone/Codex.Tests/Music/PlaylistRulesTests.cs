using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class PlaylistRulesTests
{
    private static Track T(string id, string title, string artist, string album, string genre) =>
        new(id, title, artist, album, artist, 1, 1, genre, TimeSpan.FromMinutes(3));

    private static readonly Track Gravity = T("1", "Oh! Gravity", "Switchfoot", "Oh! Gravity", "Alternative Rock");
    private static readonly Track Dare = T("2", "Dare You To Move", "Switchfoot", "The Beautiful Letdown", "Alternative Rock");
    private static readonly Track Needle = T("3", "Needle", "Bartók", "Concertos", "Classical");
    private static readonly IReadOnlyList<Track> Catalog = [Gravity, Dare, Needle];

    private static PlaylistRules Rules(RuleMatch match, params PlaylistRule[] rules) => new(match, rules);

    [Fact]
    public void Apply_MatchesAFieldExactly()
    {
        var matched = PlaylistRules.Apply(
            Rules(RuleMatch.All, new PlaylistRule(TrackField.Artist, RuleOperator.Is, "Switchfoot")), Catalog);

        Assert.Equal(["1", "2"], matched.Select(track => track.Id));
    }

    [Fact]
    public void Apply_MatchesASubstring()
    {
        var matched = PlaylistRules.Apply(
            Rules(RuleMatch.All, new PlaylistRule(TrackField.Album, RuleOperator.Contains, "letdown")), Catalog);

        Assert.Equal(["2"], matched.Select(track => track.Id));
    }

    // The same matching the filter fields use, for the same reason: nobody types the
    // accent in Bartók to find a record they own.
    [Fact]
    public void Apply_IgnoresCaseAndAccents()
    {
        var matched = PlaylistRules.Apply(
            Rules(RuleMatch.All, new PlaylistRule(TrackField.Artist, RuleOperator.Is, "bartok")), Catalog);

        Assert.Equal(["3"], matched.Select(track => track.Id));
    }

    [Fact]
    public void Apply_ExcludesWithIsNot()
    {
        var matched = PlaylistRules.Apply(
            Rules(RuleMatch.All, new PlaylistRule(TrackField.Genre, RuleOperator.IsNot, "Classical")), Catalog);

        Assert.Equal(["1", "2"], matched.Select(track => track.Id));
    }

    [Fact]
    public void Apply_UnderAllRequiresEveryRule()
    {
        var matched = PlaylistRules.Apply(
            Rules(RuleMatch.All,
                new PlaylistRule(TrackField.Artist, RuleOperator.Is, "Switchfoot"),
                new PlaylistRule(TrackField.Album, RuleOperator.Contains, "Gravity")),
            Catalog);

        Assert.Equal(["1"], matched.Select(track => track.Id));
    }

    [Fact]
    public void Apply_UnderAnyRequiresOnlyOne()
    {
        var matched = PlaylistRules.Apply(
            Rules(RuleMatch.Any,
                new PlaylistRule(TrackField.Genre, RuleOperator.Is, "Classical"),
                new PlaylistRule(TrackField.Album, RuleOperator.Contains, "Gravity")),
            Catalog);

        Assert.Equal(["1", "3"], matched.Select(track => track.Id));
    }

    // An editor with nothing in it yet shows a live count, and "1,284 tracks" for a
    // rule set that says nothing would be a startling thing to read. Empty means
    // empty, under either combinator — not the vacuous-truth answer.
    [Fact]
    public void Apply_MatchesNothingUntilARuleIsWritten()
    {
        Assert.Empty(PlaylistRules.Apply(Rules(RuleMatch.All), Catalog));
        Assert.Empty(PlaylistRules.Apply(Rules(RuleMatch.Any), Catalog));
        Assert.Empty(PlaylistRules.Apply(PlaylistRules.Empty, Catalog));
    }

    // A rule whose value has not been typed yet is the same situation mid-edit.
    [Fact]
    public void Apply_IgnoresARuleWithNoValueYet()
    {
        var matched = PlaylistRules.Apply(
            Rules(RuleMatch.All,
                new PlaylistRule(TrackField.Artist, RuleOperator.Is, "Switchfoot"),
                new PlaylistRule(TrackField.Album, RuleOperator.Contains, "   ")),
            Catalog);

        Assert.Equal(["1", "2"], matched.Select(track => track.Id));
    }

    [Fact]
    public void Apply_KeepsTheCatalogOrder()
    {
        var matched = PlaylistRules.Apply(
            Rules(RuleMatch.Any, new PlaylistRule(TrackField.Title, RuleOperator.Contains, "e")), Catalog);

        Assert.Equal(["2", "3"], matched.Select(track => track.Id));
    }

    // The rule editor's three pickers index their option arrays by casting these enums,
    // so an enum that grows without its array growing hands AccordionPicker a selection
    // that does not exist — the same crash the Friday-crate row shipped with. These
    // counts are the tripwire: if one fails, PlaylistEditor's matching array needs the
    // new entry before the enum lands.
    [Fact]
    public void RuleEnumsStillMatchTheEditorsOptionLists()
    {
        Assert.Equal(5, Enum.GetValues<TrackField>().Length);
        Assert.Equal(3, Enum.GetValues<RuleOperator>().Length);
        Assert.Equal(2, Enum.GetValues<RuleMatch>().Length);
    }
}
