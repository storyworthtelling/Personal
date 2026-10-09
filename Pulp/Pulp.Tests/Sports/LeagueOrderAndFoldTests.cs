using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// D235 (closest to season's end first; preseason and off-season last) and D241 (only the league
/// closest to season's end, and its teams with unwatched recordings, start open; what the viewer
/// opens stays open).
/// </summary>
public class LeagueOrderAndFoldTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.DateTime);

    private static League Make(string id, string name, SeasonPhase phase, DateOnly ends, DateOnly? next = null) =>
        new(new LeagueId(id), name, "badge://" + id, phase, ends, next);

    [Fact]
    public void InSeasonLeaguesComeFirstClosestToTheirEnd()
    {
        var far = Make("far", "Inland Netball", SeasonPhase.RegularSeason, Today.AddDays(90));
        var near = Make("near", "Coastal Football League", SeasonPhase.Postseason, Today.AddDays(19));
        var mid = Make("mid", "Pine Valley Lacrosse", SeasonPhase.RegularSeason, Today.AddDays(40));

        var ordered = LeagueOrder.Order([far, near, mid]);

        Assert.Equal(["near", "mid", "far"], ordered.Select(league => league.Id.Value));
    }

    [Fact]
    public void PreseasonThenOffSeasonComeLastEachBySoonestReturn()
    {
        var offLate = Make("off-late", "Lakeshore Women's Hockey", SeasonPhase.OffSeason, Today.AddDays(-30), Today.AddDays(200));
        var offSoon = Make("off-soon", "Valley Rowing", SeasonPhase.OffSeason, Today.AddDays(-60), Today.AddDays(40));
        var pre = Make("pre", "Southern Rules League", SeasonPhase.Preseason, Today.AddDays(150), Today.AddDays(3));
        var inSeason = Make("in", "Coastal Football League", SeasonPhase.RegularSeason, Today.AddDays(300));

        var ordered = LeagueOrder.Order([offLate, pre, offSoon, inSeason]);

        Assert.Equal(["in", "pre", "off-soon", "off-late"], ordered.Select(league => league.Id.Value));
    }

    [Fact]
    public void TiesFallBackToTheLeagueName()
    {
        var b = Make("b", "Bay League", SeasonPhase.RegularSeason, Today.AddDays(10));
        var a = Make("a", "Arch League", SeasonPhase.RegularSeason, Today.AddDays(10));

        Assert.Equal(["a", "b"], LeagueOrder.Order([b, a]).Select(league => league.Id.Value));
    }

    [Fact]
    public void OnlyTheLeagueClosestToItsEndStartsOpen()
    {
        var near = Make("near", "Coastal Football League", SeasonPhase.Postseason, Today.AddDays(19));
        var far = Make("far", "Inland Netball", SeasonPhase.RegularSeason, Today.AddDays(90));
        var fold = LeagueFold.Starting([far, near]);

        Assert.True(fold.IsLeagueOpen(near.Id));
        Assert.False(fold.IsLeagueOpen(far.Id));
    }

    [Fact]
    public void NoLeagueStartsOpenWhenNoneIsInSeason()
    {
        var pre = Make("pre", "Southern Rules League", SeasonPhase.Preseason, Today.AddDays(150), Today.AddDays(3));
        var off = Make("off", "Lakeshore Women's Hockey", SeasonPhase.OffSeason, Today.AddDays(-30), Today.AddDays(20));

        var fold = LeagueFold.Starting([pre, off]);

        Assert.False(fold.IsLeagueOpen(pre.Id));
        Assert.False(fold.IsLeagueOpen(off.Id));
    }

    [Fact]
    public void InTheOpenLeagueOnlyTeamsWithUnwatchedRecordingsStartOpen()
    {
        var near = Make("near", "Coastal Football League", SeasonPhase.Postseason, Today.AddDays(19));
        var far = Make("far", "Inland Netball", SeasonPhase.RegularSeason, Today.AddDays(90));
        var fold = LeagueFold.Starting([near, far]);

        Assert.True(fold.IsTeamOpen(near.Id, Gulls.Id, hasUnwatchedRecording: true));
        Assert.False(fold.IsTeamOpen(near.Id, Herons.Id, hasUnwatchedRecording: false));
        Assert.False(fold.IsTeamOpen(far.Id, Otters.Id, hasUnwatchedRecording: true));
    }

    [Fact]
    public void WhatTheViewerOpensStaysOpenAndWhatTheyCloseStaysClosed()
    {
        var near = Make("near", "Coastal Football League", SeasonPhase.Postseason, Today.AddDays(19));
        var far = Make("far", "Inland Netball", SeasonPhase.RegularSeason, Today.AddDays(90));

        var memory = FoldMemory.Empty
            .WithLeague(far.Id, open: true)
            .WithLeague(near.Id, open: false)
            .WithTeam(far.Id, Otters.Id, open: true);

        var fold = LeagueFold.Starting([near, far], memory);

        Assert.True(fold.IsLeagueOpen(far.Id));
        Assert.False(fold.IsLeagueOpen(near.Id));
        Assert.True(fold.IsTeamOpen(far.Id, Otters.Id, hasUnwatchedRecording: false));
    }

    [Fact]
    public void TheSameTeamFoldsSeparatelyInEachCompetition()
    {
        var memory = FoldMemory.Empty.WithTeam(Coastal, Gulls.Id, open: true);

        var fold = LeagueFold.Starting([], memory);

        Assert.True(fold.IsTeamOpen(Coastal, Gulls.Id, hasUnwatchedRecording: false));
        Assert.False(fold.IsTeamOpen(Southern, Gulls.Id, hasUnwatchedRecording: false));
    }
}
