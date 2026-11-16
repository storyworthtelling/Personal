using System.Text.RegularExpressions;
using Storyworthtelling.CallSheet;

namespace Storyworthtelling.CallSheet.Tests;

/// <summary>
/// The Call Sheet's own rules (audit W10.3). Every voiced line in the suite is listed once, and the rules
/// that used to live in each app's closed list hold for all of them here.
/// </summary>
public class CallSheetTests
{
    [Fact]
    public void EveryAppThatSpeaksIsOnTheSheet()
    {
        Assert.Equal(
            ["Constitution", "Flyleaf", "Folio", "Foreword", "Index", "Plate", "Post", "Pulp", "Score"],
            CallSheet.All.Select(line => line.App).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ASeatIsListedOnce()
    {
        var keys = CallSheet.All.Select(line => $"{line.App}/{line.Seat}").ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void SeatIdsAreWritten()
    {
        // A seat id is stored (the caches key on it), so it is lower-case words and hyphens, never a type's name.
        Assert.All(CallSheet.All, line => Assert.Matches("^[a-z]+(-[a-z]+)*$", line.Seat));
    }

    /// <summary>Errors are never voiced (DESIGN.md §14): no seat on the sheet is about trouble.</summary>
    [Fact]
    public void NoSeatIsAboutTrouble()
    {
        var trouble = new Regex(
            @"\b(error|errors|fail|failed|failure|problem|refused|rejected|offline|broken|couldn't|could not|warning|notice)\b",
            RegexOptions.IgnoreCase);
        Assert.All(CallSheet.All, line =>
            Assert.False(trouble.IsMatch(line.Seat + " " + line.Purpose), $"{line.App}/{line.Seat} reads as trouble"));
    }

    /// <summary>A number in an instruction is a number the model was handed without a fact behind it.</summary>
    [Fact]
    public void NoInstructionCarriesADigit()
    {
        Assert.All(CallSheet.All, line => Assert.DoesNotMatch(@"\d", line.Purpose + " " + line.Guidance));
    }

    [Fact]
    public void EveryLineHasAPurposeAndASensibleLength()
    {
        Assert.All(CallSheet.All, line =>
        {
            Assert.False(string.IsNullOrWhiteSpace(line.Purpose));
            Assert.InRange(line.MaxWords, 5, 60);
        });
    }

    [Fact]
    public void OnlyPulpSpeaksToChildren()
    {
        Assert.All(CallSheet.All.Where(line => line.Audience == VoiceAudience.Child),
            line => Assert.Equal("Pulp", line.App));
    }

    [Fact]
    public void ANeutralLineIsOnlyGivenToASeatThatShowsOne()
    {
        Assert.All(CallSheet.All.Where(line => line.Neutral is not null),
            line => Assert.Equal(VoiceFallback.Neutral, line.Fallback));
    }

    [Fact]
    public void ASeatIsFoundByAppAndId()
    {
        var line = CallSheet.For("Constitution", "today-top-line");

        Assert.Equal("Constitution", line.App);
        Assert.Contains(line, CallSheet.Of("Constitution"));
    }

    [Fact]
    public void AnUnlistedSeatIsADecisionNotADiff()
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => CallSheet.For("Pulp", "a-new-seat"));
        Assert.Contains("Call Sheet", refused.Message);
    }

    /// <summary>Constitution hides like every other app (owner's ruling, W10): no seat there shows a plain line.</summary>
    [Fact]
    public void ConstitutionHidesLikeEveryOtherApp()
    {
        Assert.All(CallSheet.Of("Constitution"), line => Assert.Equal(VoiceFallback.Hide, line.Fallback));
    }
}
