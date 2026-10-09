using System.Reflection;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Ratings;
using Pulp.Domain.Voice;

namespace Pulp.Tests.Voice;

/// <summary>
/// G12 — what each seat may be told. Invented household: Rowan (the device owner), Ash (the other
/// adult), Pip (a child). Invented titles throughout.
/// </summary>
public class VoiceFactBuilderTests
{
    private static readonly ViewerId Rowan = new("rowan");
    private static readonly ViewerId Ash = new("ash");
    private static readonly ViewerId Pip = new("pip");
    private static readonly DateTimeOffset Evening = new(2026, 10, 9, 19, 30, 0, TimeSpan.FromHours(-5));

    // ---- why-this (D36, D41, D23) ----

    [Fact]
    public void WhyThisNamesOnePastTitleTheOwnerFinishedTheMostRecentOne()
    {
        var facts = VoiceFactBuilder.WhyThis(Rowan, ViewerStanding.DeviceOwner, "Harbour Lights",
        [
            new PastTitle(Rowan, "The Copper Kettle", Evening.AddDays(-40)),
            new PastTitle(Rowan, "The Brass Lantern Mysteries", Evening.AddDays(-3)),
            new PastTitle(Rowan, "Saltmarsh", Evening.AddDays(-12)),
        ]);

        var text = string.Join(" ", facts.Lines);
        Assert.Contains("The Brass Lantern Mysteries", text);
        Assert.DoesNotContain("The Copper Kettle", text);
        Assert.DoesNotContain("Saltmarsh", text);
        Assert.Null(facts.ReadingLevel);
    }

    [Fact]
    public void WhyThisNeverNamesAnotherViewersHistory()
    {
        var facts = VoiceFactBuilder.WhyThis(Rowan, ViewerStanding.DeviceOwner, "Harbour Lights",
        [
            new PastTitle(Ash, "Ash's Secret Show", Evening.AddDays(-1)),
            new PastTitle(Pip, "Pip's Cartoon", Evening.AddHours(-2)),
            new PastTitle(Rowan, "Saltmarsh", Evening.AddDays(-12)),
        ]);

        var text = string.Join(" ", facts.Lines);
        Assert.Contains("Saltmarsh", text);
        Assert.DoesNotContain("Ash's Secret Show", text);
        Assert.DoesNotContain("Pip's Cartoon", text);
    }

    [Fact]
    public void WhyThisForTheOtherAdultNamesNoPastTitleAtAll()
    {
        var facts = VoiceFactBuilder.WhyThis(Ash, ViewerStanding.OtherAdult, "Harbour Lights",
            [new PastTitle(Ash, "Saltmarsh", Evening.AddDays(-2))]);

        Assert.True(facts.HasFacts);
        Assert.DoesNotContain("Saltmarsh", string.Join(" ", facts.Lines));
    }

    [Fact]
    public void WhyThisForAChildHasNoFactsSoTheLineIsHidden()
    {
        var facts = VoiceFactBuilder.WhyThis(Pip, ViewerStanding.Child, "Harbour Lights",
            [new PastTitle(Pip, "Pip's Cartoon", Evening.AddDays(-1))]);

        Assert.False(facts.HasFacts);
        Assert.Equal(VoiceLineAction.Hide, VoiceLinePlan.Decide(facts, voiceReady: true, remembered: null, Today));
    }

    // ---- adult seats speak to adults ----

    [Fact]
    public void EveryAdultSeatCarriesNoReadingLevel()
    {
        var all = AdultFacts();
        Assert.Equal(10, all.Count);
        Assert.All(all, facts =>
        {
            Assert.Equal(VoiceAudience.Adult, facts.Occasion.Audience);
            Assert.Null(facts.ReadingLevel);
        });
    }

    [Fact]
    public void TheGreetingCountsInWordsAndSaysWhenItIs()
    {
        var facts = VoiceFactBuilder.Greeting(Evening, firstOpenOfDay: true, inProgress: 3, arrivedFriday: 5);
        var text = string.Join(" ", facts.Lines);
        Assert.Contains("evening", text);
        Assert.Contains("three", text);
        Assert.Contains("five", text);
        Assert.Contains("first", text);
        Assert.DoesNotMatch("[0-9]", text);
    }

    // ---- child seats (D39, D232, D233) ----

    private static readonly ChildRules PipRules = new(Pip, AgeBand.SevenToNine);

    [Fact]
    public void EveryChildSeatCarriesTheChildsAgeBandAsItsReadingLevel()
    {
        var all = new[]
        {
            ChildVoiceFacts.Greeting(PipRules, Evening, firstOpenOfDay: true),
            ChildVoiceFacts.Favorites(PipRules, [Named("t-otter", "Otter Island", "TV-Y")]),
            ChildVoiceFacts.EmptyFavorites(PipRules),
        };

        Assert.All(all, facts =>
        {
            Assert.Equal(VoiceAudience.Child, facts.Occasion.Audience);
            Assert.Equal(AgeBand.SevenToNine, facts.ReadingLevel);
            Assert.Contains("seven", facts.Context);
            Assert.Contains("nine", facts.Context);
        });
    }

    [Fact]
    public void AChildsFavoritesLineNeverNamesATitleTheirFilterHides()
    {
        var facts = ChildVoiceFacts.Favorites(PipRules,
        [
            Named("t-night", "Nightwatch Harbour", "TV-MA"),
            Named("t-unrated", "Unrated Thing", "N/A"),
            Named("t-otter", "Otter Island", "TV-Y"),
            Named("t-biscuit", "Biscuit and the Map", "TV-Y7"),
        ]);

        var text = string.Join(" ", facts.Lines);
        Assert.DoesNotContain("Nightwatch Harbour", text);
        Assert.DoesNotContain("Unrated Thing", text);
        Assert.Contains("Otter Island", text);
        Assert.DoesNotContain("Biscuit and the Map", text);
    }

    [Fact]
    public void AChildsFavoritesLineWithNothingVisibleNamesNothing()
    {
        var facts = ChildVoiceFacts.Favorites(PipRules, [Named("t-night", "Nightwatch Harbour", "R")]);
        Assert.DoesNotContain("Nightwatch Harbour", string.Join(" ", facts.Lines));
    }

    /// <summary>
    /// A child's seat is told only what a child's view already holds. Reflective: every builder for a
    /// child's seat takes nothing but the child's own rules, their (filtered) favorites, the clock and
    /// first-open — no past titles, no viewer ids, no counts from an adult's stack. The bug this
    /// reintroduces is a child builder taking an adult's history "just to say something nicer".
    /// </summary>
    [Fact]
    public void AChildSeatsBuilderTakesNoAdultOnlyFacts()
    {
        var allowed = new HashSet<Type>
        {
            typeof(ChildRules), typeof(IEnumerable<NamedTitle>), typeof(DateTimeOffset), typeof(bool),
        };

        var methods = typeof(ChildVoiceFacts).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(3, methods.Length);

        foreach (var method in methods)
        {
            Assert.Equal(typeof(VoiceFacts), method.ReturnType);
            Assert.Equal(typeof(ChildRules), method.GetParameters()[0].ParameterType);
            foreach (var parameter in method.GetParameters())
            {
                Assert.True(allowed.Contains(parameter.ParameterType),
                    $"ChildVoiceFacts.{method.Name} takes {parameter.ParameterType.Name} {parameter.Name}, "
                    + "which a child's seat must never be told (D23, D41, D195).");
            }
        }
    }

    /// <summary>The adult builder makes no child seat, so a child line cannot skip its reading level.</summary>
    [Fact]
    public void TheAdultBuilderNeverReturnsAChildSeat()
    {
        var methods = typeof(VoiceFactBuilder).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.NotEmpty(methods);
        Assert.DoesNotContain(methods, method => method.GetParameters().Any(p => p.ParameterType == typeof(ChildRules)));
        Assert.All(AdultFacts(), facts => Assert.NotEqual(VoiceAudience.Child, facts.Occasion.Audience));
    }

    [Fact]
    public void TheFactsHashChangesWithTheFactsAndNotOtherwise()
    {
        var one = VoiceFactBuilder.Playlist("Rainy Sundays", entries: 6, watched: 2);
        var same = VoiceFactBuilder.Playlist("Rainy Sundays", entries: 6, watched: 2);
        var moved = VoiceFactBuilder.Playlist("Rainy Sundays", entries: 6, watched: 3);
        Assert.Equal(one.Hash, same.Hash);
        Assert.NotEqual(one.Hash, moved.Hash);
    }

    internal static readonly DateOnly Today = new(2026, 10, 9);

    private static List<VoiceFacts> AdultFacts() =>
    [
        VoiceFactBuilder.Greeting(Evening, firstOpenOfDay: false, inProgress: 2, arrivedFriday: 0),
        VoiceFactBuilder.WhyThis(Rowan, ViewerStanding.DeviceOwner, "Harbour Lights", []),
        VoiceFactBuilder.Playlist("Rainy Sundays", entries: 6, watched: 2),
        VoiceFactBuilder.EmptyContinueWatching(arrivedFriday: 5),
        VoiceFactBuilder.EmptyPlaylists(),
        VoiceFactBuilder.EmptyPlaylist("Rainy Sundays"),
        VoiceFactBuilder.EmptyRecentlyWatched(),
        VoiceFactBuilder.EmptyCalendar(daysAhead: 14),
        VoiceFactBuilder.EmptySports(),
        VoiceFactBuilder.StackUsedUp(Today),
    ];

    private static NamedTitle Named(string id, string name, string rating) =>
        new(new TitleFacts(new TitleId(id), TitleKind.Show, ContentRating.Parse(rating)), name);
}
