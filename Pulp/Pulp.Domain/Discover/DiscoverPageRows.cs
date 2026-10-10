using System.Globalization;
using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;

namespace Pulp.Domain.Discover;

/// <summary>
/// One title as Discover shows it: the front card of a pile, and — by the same anatomy, since search
/// and mood results have no mockup of their own (D272) — one search or mood result. <see cref="Tags"/>
/// are the title's genres, which a veto remembers so taste learns from it (D24).
/// </summary>
public sealed record DiscoverRow(
    TitleId Id,
    TitleKind Kind,
    string Name,
    string KindLine,
    string HomeLine,
    bool OnYourServices,
    string? PosterUrl,
    IReadOnlyList<string> Tags)
{
    /// <summary>The release year, which the trailer lookup is narrowed by; null when the source gave none.</summary>
    public int? Year { get; init; }
}

/// <summary>
/// The text mockup 03 draws beside the front poster: "Film · 1h 44m" or "Show · 2 seasons", then
/// where it plays — "On Streamly · 4K", or "On Lanternbox · with ads" where the plan has ads (D29).
/// The home is <see cref="BestHome"/>'s answer (D28, D216). A title off the household's services
/// says where it streams instead (D107, D20); one that streams nowhere says so, and points at the
/// rent line its title page carries (D53).
/// </summary>
public static class DiscoverPageRows
{
    public const string Separator = " · ";
    public const string WithAds = "with ads";
    public const string NotYours = "not yours";
    public const string NotStreaming = "Not streaming";
    public const string RentOrBuy = "rent or buy";

    public static DiscoverRow For(CatalogueTitle title, IEnumerable<ServicePlan> household, HomeOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(overrides);

        return new DiscoverRow(
            title.Id,
            title.Kind,
            title.Name.Trim(),
            KindLine(title),
            HomeLine(title, household.ToList(), overrides, out var yours),
            yours,
            string.IsNullOrWhiteSpace(title.PosterUrl) ? null : title.PosterUrl,
            title.Genres.Where(genre => !string.IsNullOrWhiteSpace(genre)).ToList())
        {
            Year = title.Year,
        };
    }

    public static string KindLine(CatalogueTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return title.Kind switch
        {
            TitleKind.Film => title.RuntimeMinutes is > 0 and var minutes ? "Film" + Separator + Runtime(minutes) : "Film",
            TitleKind.Show => title.SeasonCount is > 0 and var seasons ? "Show" + Separator + Seasons(seasons) : "Show",
            _ => throw new ArgumentOutOfRangeException(nameof(title), title.Kind, "A title kind with no line."),
        };
    }

    public static string Quality(VideoQuality quality) => quality switch
    {
        VideoQuality.Uhd => "4K",
        VideoQuality.Hd => "HD",
        VideoQuality.Sd => "SD",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "A picture quality with no label."),
    };

    private static string Runtime(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours == 0)
        {
            return rest.ToString(CultureInfo.InvariantCulture) + "m";
        }

        return rest == 0
            ? hours.ToString(CultureInfo.InvariantCulture) + "h"
            : string.Create(CultureInfo.InvariantCulture, $"{hours}h {rest}m");
    }

    private static string Seasons(int count) =>
        count == 1 ? "1 season" : count.ToString(CultureInfo.InvariantCulture) + " seasons";

    private static string HomeLine(CatalogueTitle title, IReadOnlyList<ServicePlan> household, HomeOverrides overrides, out bool yours)
    {
        var choice = BestHome.Choose(title.Id, TitleOffers.Streams(title.Offers), household, overrides);
        if (choice.Best is { } best)
        {
            yours = true;
            var name = NameOf(title, best.Service);
            return "On " + name + Separator + (best.WithAds ? WithAds : Quality(best.Quality));
        }

        yours = false;
        var ours = household.Select(plan => plan.Service).ToHashSet();
        var elsewhere = title.Offers
            .Where(offer => offer.Kind is OfferKind.Subscription or OfferKind.Free && !ours.Contains(offer.Service))
            .OrderByDescending(offer => offer.Quality)
            .ThenBy(offer => offer.ServiceName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(offer => !string.IsNullOrWhiteSpace(offer.ServiceName));
        if (elsewhere is not null)
        {
            return "On " + elsewhere.ServiceName.Trim() + Separator + NotYours;
        }

        return TitleOffers.Paid(title.Offers).Count > 0 ? NotStreaming + Separator + RentOrBuy : NotStreaming;
    }

    private static string NameOf(CatalogueTitle title, ServiceId service) =>
        title.Offers.FirstOrDefault(offer => offer.Service == service && !string.IsNullOrWhiteSpace(offer.ServiceName))?.ServiceName.Trim()
        ?? service.Value;
}
