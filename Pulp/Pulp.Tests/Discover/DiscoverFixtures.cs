using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.People;
using Pulp.Domain.Ratings;
using Pulp.Domain.Services;

namespace Pulp.Tests.Discover;

/// <summary>An invented household, services and catalogue for Discover's mood search and playlist tests.</summary>
internal static class DiscoverFixtures
{
    public static readonly StreamingService Flix = new(
        new ServiceId("example-flix"), "Exampleflix", ["com.example.flix"], "exampleflix", ["Exampleflix"]);

    public static readonly StreamingService Sample = new(
        new ServiceId("sample-stream"), "Samplestream", ["com.example.sample"], "samplestream", ["Samplestream"]);

    /// <summary>A service the household does not subscribe to.</summary>
    public static readonly StreamingService Elsewhere = new(
        new ServiceId("elsewhere-tv"), "Elsewhere TV", ["com.example.elsewhere"], "elsewheretv", ["Elsewhere TV"]);

    public static readonly IReadOnlyList<StreamingService> HouseholdServices = [Flix, Sample];

    public static readonly ViewerId Sam = new("adult-example-a");
    public static readonly ViewerId Wren = new("child-example-wren");

    public static readonly HouseholdRoster Roster = new(
    [
        new HouseholdViewer(Sam, IsChild: false),
        new HouseholdViewer(Wren, IsChild: true),
    ]);

    public static readonly FranchiseId Harbour = new("franchise-harbour-tales");

    public static readonly ChildRules WrenRules = new(Wren, AgeBand.SevenToNine)
    {
        BlockedFranchises = new HashSet<FranchiseId> { Harbour },
    };

    public static readonly CatalogueTitle Glasshouse = Title("title-glasshouse", "The Glasshouse Year", 2019, TitleKind.Film, "PG", Flix);
    public static readonly CatalogueTitle Kettle = Title("title-kettle", "Kettle & Crumb", 2021, TitleKind.Show, "TV-Y7", Sample);
    public static readonly CatalogueTitle Nightwater = Title("title-nightwater", "Nightwater", 2017, TitleKind.Film, "R", Flix);
    public static readonly CatalogueTitle Lantern = Title("title-lantern-cove", "Lantern Cove", 2020, TitleKind.Show, "TV-G", Sample);
    public static readonly CatalogueTitle Unrated = Title("title-quiet-field", "The Quiet Field", 2022, TitleKind.Film, null, Flix);
    public static readonly CatalogueTitle OffServices = Title("title-far-shore", "Far Shore", 2018, TitleKind.Film, "PG", Elsewhere);

    /// <summary>A remake: the same name as <see cref="Glasshouse"/>, a different year.</summary>
    public static readonly CatalogueTitle GlasshouseRemake = Title("title-glasshouse-2024", "The Glasshouse Year", 2024, TitleKind.Film, "PG-13", Flix);

    public static readonly IReadOnlyList<CatalogueTitle> KnownTitles =
        [Glasshouse, Kettle, Nightwater, Lantern, Unrated, OffServices, GlasshouseRemake];

    /// <summary>Only <see cref="Lantern"/> is in the blocked franchise.</summary>
    public static IReadOnlyCollection<FranchiseId> FranchisesOf(TitleId title) =>
        title == Lantern.Id ? [Harbour] : [];

    public static CatalogueTitle Title(string id, string name, int? year, TitleKind kind, string? rating, StreamingService service) => new()
    {
        Id = new TitleId(id),
        Kind = kind,
        Name = name,
        Year = year,
        Genres = ["Drama"],
        Rating = ContentRating.Parse(rating),
        Offers =
        [
            new TitleOffer(service.Id, service.Name, true, OfferKind.Subscription, VideoQuality.Hd, null, null,
                "https://example.com/watch/" + id, null, null, false),
        ],
    };
}
