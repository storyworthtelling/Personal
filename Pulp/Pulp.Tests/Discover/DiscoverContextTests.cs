using System.Reflection;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Friday;
using static Pulp.Tests.Discover.DiscoverFixtures;

namespace Pulp.Tests.Discover;

/// <summary>
/// What a mood search or a made playlist sends out (D104, D171): the mood or seed, the household's
/// services by name, and the adult viewer's taste tags — never a title anyone watched, never anything
/// about a child, never an id.
/// </summary>
public class DiscoverContextTests
{
    private static readonly TasteProfile SamTaste = TasteProfile.From(new Dictionary<string, int>
    {
        ["slow-burn"] = 4,
        ["coastal"] = 2,
        ["heist"] = -3,
        ["musical"] = 0,
    });

    [Fact]
    public void AnAdultSendsTheirLikedTagsStrongestFirstAndNoneTheyDislike()
    {
        var context = DiscoverContext.For(MoodViewer.Adult(Sam), Roster, HouseholdServices, SamTaste);

        Assert.Equal(["slow-burn", "coastal"], context.TasteTags);
        Assert.False(context.ForChildren);
    }

    [Fact]
    public void TheTasteTagsAreCapped()
    {
        var many = TasteProfile.From(Enumerable.Range(1, 30).ToDictionary(i => "tag-" + i.ToString("00"), i => i));

        var context = DiscoverContext.For(MoodViewer.Adult(Sam), Roster, HouseholdServices, many);

        Assert.Equal(DiscoverContext.MaxTasteTags, context.TasteTags.Count);
        Assert.Equal("tag-30", context.TasteTags[0]);
    }

    [Fact]
    public void TheHouseholdServicesAreSentByNameAndKeptByIdForResolving()
    {
        var context = DiscoverContext.For(MoodViewer.Adult(Sam), Roster, HouseholdServices, SamTaste);

        Assert.Equal(["Exampleflix", "Samplestream"], context.ServiceNames);
        Assert.Equal([Flix.Id, Sample.Id], context.Services);
    }

    [Fact]
    public void AChildSendsNoTasteEvenWhenAProfileIsHandedIn()
    {
        var context = DiscoverContext.For(MoodViewer.Child(WrenRules), Roster, HouseholdServices, SamTaste);

        Assert.Empty(context.TasteTags);
        Assert.True(context.ForChildren);
        Assert.Equal(["Exampleflix", "Samplestream"], context.ServiceNames);
    }

    [Fact]
    public void ASittingWithAChildSendsNoAdultTaste()
    {
        var context = DiscoverContext.For(MoodViewer.WithChildren(Sam, [WrenRules]), Roster, HouseholdServices, SamTaste);

        Assert.Empty(context.TasteTags);
    }

    [Fact]
    public void AChildTreatedAsAnAdultIsRefused()
    {
        // The roster says Wren is a child; a view without Wren's filter would show her everything.
        Assert.Throws<ArgumentException>(() =>
            DiscoverContext.For(MoodViewer.Adult(Wren), Roster, HouseholdServices, TasteProfile.Empty));
    }

    [Fact]
    public void AViewerTheRosterDoesNotNameIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            DiscoverContext.For(MoodViewer.Adult(new ViewerId("stranger-example")), Roster, HouseholdServices, SamTaste));
    }

    [Fact]
    public void AMoodIsTrimmedCappedAndNeverBlank()
    {
        var context = DiscoverContext.For(MoodViewer.Adult(Sam), Roster, HouseholdServices, SamTaste);

        Assert.Equal("rainy and quiet", MoodQuery.For("  rainy and quiet  ", context).Mood);
        Assert.Equal(MoodQuery.MaxMoodLength, MoodQuery.For(new string('a', 500), context).Mood.Length);
        Assert.Throws<ArgumentException>(() => MoodQuery.For("   ", context));
    }

    /// <summary>
    /// The privacy guard, structural: nothing that is sent can hold a title id, a viewer id or a
    /// child's rules, so no caller can put a child's viewing into a request by accident.
    /// </summary>
    [Fact]
    public void NothingSentCanCarryATitleAViewerOrAChildsRules()
    {
        Type[] sent = [typeof(MoodQuery), typeof(DiscoverContext), typeof(PlaylistQuery), typeof(PlaylistSeed), typeof(SeedTitle)];
        Type[] forbidden = [typeof(TitleId), typeof(ViewerId), typeof(Pulp.Domain.Children.ChildRules), typeof(MoodViewer)];

        var checkedMembers = 0;
        var offenders = new List<string>();
        foreach (var type in sent)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                checkedMembers++;
                if (forbidden.Any(bad => Mentions(property.PropertyType, bad)))
                {
                    offenders.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        Assert.True(checkedMembers >= 10, $"Only {checkedMembers} members were checked; the guard is not reading the sent types.");
        Assert.True(offenders.Count == 0, "A sent type can carry a private value: " + string.Join(", ", offenders));
    }

    private static bool Mentions(Type type, Type bad) =>
        type == bad || (type.IsGenericType && type.GetGenericArguments().Any(argument => Mentions(argument, bad)))
        || (type.IsArray && Mentions(type.GetElementType()!, bad));
}
