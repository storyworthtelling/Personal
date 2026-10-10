using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.People;
using Pulp.Domain.Ratings;

namespace Pulp.Tests.ChildView;

/// <summary>An invented household for the child's-view tests: one adult, two children.</summary>
internal static class ChildViewFixtures
{
    public static readonly ViewerId Parent = new("parent-example-a");
    public static readonly ViewerId Robin = new("Robin");
    public static readonly ViewerId Sam = new("Sam");

    public static readonly TitleId Biscuit = new("biscuit-example");
    public static readonly TitleId Bugs = new("bugs-example");
    public static readonly TitleId Tidewater = new("tidewater-example");
    public static readonly TitleId Cloudberry = new("cloudberry-example");
    public static readonly TitleId Grim = new("grim-example");

    public static readonly DateTimeOffset Now = new(2026, 9, 4, 18, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset DaysAgo(int days) => Now.AddDays(-days);

    public static HouseholdRoster Roster() =>
        new([new HouseholdViewer(Parent, IsChild: false), new HouseholdViewer(Robin, IsChild: true), new HouseholdViewer(Sam, IsChild: true)]);

    public static ChildRules RobinRules(AgeBand ceiling = AgeBand.SevenToNine) => new(Robin, ceiling);

    public static ChildRules SamRules(AgeBand ceiling = AgeBand.UnderSeven) => new(Sam, ceiling);

    public static TitleFacts Rated(TitleId title, string rating) => new(title, TitleKind.Show, ContentRating.Parse(rating));
}
