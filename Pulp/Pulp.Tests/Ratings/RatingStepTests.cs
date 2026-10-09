using System.Reflection;
using Pulp.Domain.Ratings;

namespace Pulp.Tests.Ratings;

/// <summary>
/// The four-step rating chip (D215, amended by D276): kids · family · teen · mature, read from MPA
/// film and US TV ratings, with unrated as a hairline outline. Each step carries its own label, so the
/// chip reads without colour.
/// </summary>
public class RatingStepTests
{
    [Theory]
    [InlineData("G", RatingStep.Kids)]
    [InlineData("TV-Y", RatingStep.Kids)]
    [InlineData("TV-Y7", RatingStep.Kids)]
    [InlineData("TV-Y7-FV", RatingStep.Kids)]
    [InlineData("PG", RatingStep.Family)]
    [InlineData("TV-G", RatingStep.Family)]
    [InlineData("TV-PG", RatingStep.Family)]
    [InlineData("PG-13", RatingStep.Teen)]
    [InlineData("TV-14", RatingStep.Teen)]
    [InlineData("R", RatingStep.Mature)]
    [InlineData("NC-17", RatingStep.Mature)]
    [InlineData("TV-MA", RatingStep.Mature)]
    public void EveryMpaAndTvRatingLandsOnItsStep(string raw, RatingStep expected)
    {
        var rating = ContentRating.Parse(raw);

        Assert.True(rating.IsRated);
        Assert.Equal(expected, rating.Step);
    }

    [Theory]
    [InlineData("pg-13", "PG-13", RatingSystem.Mpa)]
    [InlineData("  tv-14 ", "TV-14", RatingSystem.UsTv)]
    [InlineData("TV MA", "TV-MA", RatingSystem.UsTv)]
    [InlineData("PG–13", "PG-13", RatingSystem.Mpa)]
    public void ARatingIsReadWhateverItsCasingOrSpacing(string raw, string code, RatingSystem system)
    {
        var rating = ContentRating.Parse(raw);

        Assert.Equal(code, rating.Code);
        Assert.Equal(system, rating.System);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("N/A")]
    [InlineData("Not Rated")]
    [InlineData("Unrated")]
    [InlineData("Approved")]
    [InlineData("Passed")]
    [InlineData("TV-13")]
    [InlineData("M/PG")]
    public void AnythingOutsideTheTwoSystemsIsUnratedNeverGuessed(string? raw)
    {
        var rating = ContentRating.Parse(raw);

        Assert.False(rating.IsRated);
        Assert.Null(rating.Step);
        Assert.Equal(RatingSystem.None, rating.System);
    }

    [Fact]
    public void AnOldXRatingIsMatureNotUnrated() =>
        Assert.Equal(RatingStep.Mature, ContentRating.Parse("X").Step);

    [Theory]
    [InlineData(RatingStep.Kids, ChipRole.Positive)]
    [InlineData(RatingStep.Family, ChipRole.Resolved)]
    [InlineData(RatingStep.Teen, ChipRole.Reference)]
    [InlineData(RatingStep.Mature, ChipRole.Problem)]
    public void EachStepTakesItsRole(RatingStep step, ChipRole role) =>
        Assert.Equal(role, RatingChip.RoleOf(step));

    [Fact]
    public void TheChipNamesTheRatingAndFillsByStep()
    {
        var chip = RatingChip.For(ContentRating.Parse("tv-14"));

        Assert.Equal("TV-14", chip.Label);
        Assert.Equal(RatingStep.Teen, chip.Step);
        Assert.Equal(ChipRole.Reference, chip.Role);
        Assert.False(chip.Outlined);
    }

    [Fact]
    public void AnUnratedChipIsAnOutlineWithNoRole()
    {
        var chip = RatingChip.For(ContentRating.Parse("Not Rated"));

        Assert.True(chip.Outlined);
        Assert.Null(chip.Role);
        Assert.Null(chip.Step);
        Assert.False(string.IsNullOrWhiteSpace(chip.Label));
    }

    [Fact]
    public void TheFourStepsHaveFourDistinctLabelsSoTheChipReadsWithoutColour()
    {
        var labels = new[] { "G", "PG", "PG-13", "R" }
            .Select(code => RatingChip.For(ContentRating.Parse(code)))
            .Select(chip => (chip.Step, chip.Label))
            .ToList();

        Assert.Equal(4, labels.Select(entry => entry.Step).Distinct().Count());
        Assert.Equal(4, labels.Select(entry => entry.Label).Distinct().Count());
    }

    /// <summary>
    /// D275/D276: Pulp never draws Caution. The chip's role is a closed enum of the four roles it may
    /// use, so a fifth — Caution, which the gate first drew for teen — cannot be handed out at all.
    /// </summary>
    [Fact]
    public void TheChipRoleCannotNameCaution()
    {
        var names = Enum.GetNames<ChipRole>();

        Assert.Equal(4, names.Length);
        Assert.DoesNotContain(names, name => name.Contains("Caution", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            Enum.GetValues<RatingStep>().Select(RatingChip.RoleOf).Distinct().Count(),
            Enum.GetValues<RatingStep>().Length);
    }

    [Fact]
    public void ChipRoleDeclaresNoMemberBeyondItsFourRoles()
    {
        var fields = typeof(ChipRole).GetFields(BindingFlags.Public | BindingFlags.Static);

        Assert.Equal(["Positive", "Resolved", "Reference", "Problem"], fields.Select(field => field.Name));
    }
}
