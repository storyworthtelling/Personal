using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Snowball;

/// <summary>Pure unit tests for the fixture-name matching rule the one-time history import (Guard G8)
/// relies on, isolated from the database entirely.</summary>
public class SnowballHistoryMatchTests
{
    [Fact]
    public void ExactAccountNameWins_EvenWhenAnotherAccountsNameIsASubstringMatch()
    {
        var candidates = new[]
        {
            new SnowballHistoryCandidate(Guid.NewGuid(), "Redwood Bank - Bluebird", "Redwood Bank"),
            new SnowballHistoryCandidate(Guid.NewGuid(), "Redwood Bank - Skyline", "Redwood Bank"),
        };

        var matched = SnowballHistoryMatch.Resolve("Redwood Bank - Skyline", candidates);

        Assert.Equal(candidates[1].AccountId, matched);
    }

    [Fact]
    public void FallsBackToTheInstitutionNameWhenNoAccountNameMatches()
    {
        var candidates = new[]
        {
            new SnowballHistoryCandidate(Guid.NewGuid(), "Mortgage", "Maple Ridge Mortgage"),
        };

        var matched = SnowballHistoryMatch.Resolve("Maple Ridge Mortgage", candidates);

        Assert.Equal(candidates[0].AccountId, matched);
    }

    [Fact]
    public void FallsBackToASubstringMatch_WhenNothingMatchesExactly()
    {
        var candidates = new[]
        {
            new SnowballHistoryCandidate(Guid.NewGuid(), "Lakeside CU Personal Loan", "Lakeside Credit Union"),
        };

        var matched = SnowballHistoryMatch.Resolve("Lakeside CU", candidates);

        Assert.Equal(candidates[0].AccountId, matched);
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        var candidates = new[] { new SnowballHistoryCandidate(Guid.NewGuid(), "meridian card", "meridian card") };

        Assert.Equal(candidates[0].AccountId, SnowballHistoryMatch.Resolve("Meridian Card", candidates));
    }

    [Fact]
    public void ReturnsNull_WhenNothingMatchesAtAll()
    {
        var candidates = new[] { new SnowballHistoryCandidate(Guid.NewGuid(), "Checking", "Redwood Bank") };

        Assert.Null(SnowballHistoryMatch.Resolve("Evergreen Home Lending", candidates));
    }

    [Fact]
    public void ReturnsNull_WhenThereAreNoCandidatesAtAll()
    {
        Assert.Null(SnowballHistoryMatch.Resolve("Maple Ridge Mortgage", []));
    }
}
