using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Ratings;
using Pulp.Domain.Services;

namespace Pulp.Tests.HandOff;

/// <summary>An invented household and catalogue for the hand-off planner's tests.</summary>
internal static class HandOffFixtures
{
    public static readonly StreamingService Flix = new(
        new ServiceId("example-flix"), "Exampleflix", ["com.example.flix", "com.example.flix.legacy"], "exampleflix", ["Exampleflix"]);

    public static readonly StreamingService Sample = new(
        new ServiceId("sample-stream"), "Samplestream", ["com.example.sample"], "samplestream", ["Samplestream"]);

    public static readonly StreamingService Tots = new(
        new ServiceId("example-tots"), "Exampletots", ["com.example.tots"], "exampletots", ["Exampletots"], kidsOnly: true);

    public static readonly ServiceCatalogue TestCatalogue = new([Flix, Sample, Tots]);

    public static readonly ViewerId Sam = new("adult-example-a");
    public static readonly ViewerId Wren = new("child-example-wren");
    public static readonly ViewerId Moss = new("child-example-moss");

    public static readonly HouseholdRoster Roster = new(
    [
        new HouseholdViewer(Sam, IsChild: false),
        new HouseholdViewer(Wren, IsChild: true),
        new HouseholdViewer(Moss, IsChild: true),
    ]);

    public static readonly ChildRules WrenRules = new(Wren, AgeBand.SevenToNine);
    public static readonly ChildRules MossRules = new(Moss, AgeBand.ThirteenToSixteen);

    public static readonly TitleId Lantern = new("title-lantern-cove");

    public static readonly DateTimeOffset At = new(2026, 3, 6, 19, 30, 0, TimeSpan.FromHours(-5));

    public static HandOffPlanner Planner(params ServiceId[] attested) =>
        new(Roster, [WrenRules, MossRules], TestCatalogue, new AttestedServices(attested));

    public static TitleFacts Facts(params StreamingService[] homes) =>
        new(Lantern, TitleKind.Show, ContentRating.Parse("TV-Y")) { Homes = homes.Select(home => home.Id).ToList() };

    public static HandOffLink Link(StreamingService service) =>
        new(Lantern, service.Id, new Uri("https://watch.example.com/title/100"), null);

    public static HandOffRequest Request(StreamingService service, params ViewerId[] watching) =>
        new(watching, Facts(Flix, Sample, Tots), Link(service), ["Lantern Cove"]);
}
