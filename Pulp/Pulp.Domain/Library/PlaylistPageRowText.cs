using System.Globalization;
using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;

namespace Pulp.Domain.Library;

/// <summary>
/// A playlist row's words (mockup 04): the <see cref="Name"/> (null until the title has been looked
/// up), the <see cref="KindLine"/> under it, and the quieter <see cref="WhereLine"/>.
/// <see cref="WhereEmphasis"/> marks "not on your services", which the mockup sets a shade darker.
/// </summary>
public sealed record PlaylistRowLines(string? Name, string KindLine, string? WhereLine, bool WhereEmphasis);

/// <summary>
/// What a playlist row says (mockup 04, D225).
/// <list type="bullet">
/// <item>The kind and length: "Film · 1h 44m", "Show · 3 seasons".</item>
/// <item>Where it plays, chosen by <see cref="BestHome"/> (D28, D29, D216): "On Streamly · 4K", "On Kestrel TV · free, with ads".</item>
/// <item>Leaving soon: the date sits on the row only, in place of the service line (D124, D146).</item>
/// <item>Not on the household's services: that, plus <see cref="RentLine"/>'s one line (D20, D53).</item>
/// <item>A struck row adds "· watched" and drops the place (D101); a caught-up show reads "Caught up ·
/// Season N out &lt;date&gt;" (D224); a film not out yet carries its date (D32). A dimmed row names no place.</item>
/// </list>
/// Over bare service ids a subscription is taken as ad-free and only a free service's offer says "with
/// ads"; over the household's plans a recorded with-ads tier says so too.
/// </summary>
public static class PlaylistPageRowText
{
    private const string Dot = " · ";

    public static PlaylistRowLines For(
        PlaylistRow row,
        CatalogueTitle? title,
        IReadOnlyList<ServiceId> householdServices,
        HomeOverrides overrides,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(householdServices);
        return For(row, title, householdServices.Distinct().Select(service => new ServicePlan(service, AdFree: true)).ToList(), overrides, today);
    }

    /// <summary>
    /// The row over the household's recorded plans (D29): a with-ads subscription says "On Streamly · HD,
    /// with ads" and a plan's picture caps the 4K the row may claim.
    /// </summary>
    public static PlaylistRowLines For(
        PlaylistRow row,
        CatalogueTitle? title,
        IReadOnlyList<ServicePlan> plans,
        HomeOverrides overrides,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(overrides);

        var kind = KindAndLength(row.Entry.Kind, title);
        var name = title?.Name;

        if (row.Struck)
        {
            return new PlaylistRowLines(name, kind + Dot + "watched", WhereLine: null, WhereEmphasis: false);
        }

        switch (row.Dim)
        {
            case RowDim.CaughtUp:
                var caughtUp = row.NextSeason is { } season && row.DimUntil is { } lands
                    ? "Caught up" + Dot + $"Season {season} out " + Day(lands)
                    : "Caught up";
                return new PlaylistRowLines(name, caughtUp, WhereLine: null, WhereEmphasis: false);
            case RowDim.NotOutYet:
                var notOut = row.DimUntil is { } release ? kind + Dot + "out " + Day(release) : kind;
                return new PlaylistRowLines(name, notOut, WhereLine: null, WhereEmphasis: false);
            case RowDim.None:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(row), row.Dim, "A row dim with no words decided.");
        }

        if (title is null || row.Entry.Kind is PlaylistEntryKind.Game or PlaylistEntryKind.Franchise)
        {
            return new PlaylistRowLines(name, kind, WhereLine: null, WhereEmphasis: false);
        }

        var (where, emphasis) = Where(title, plans, overrides);
        return new PlaylistRowLines(name, kind, where, emphasis);
    }

    private static string KindAndLength(PlaylistEntryKind kind, CatalogueTitle? title) => kind switch
    {
        PlaylistEntryKind.Film => title?.RuntimeMinutes is { } minutes and > 0 ? "Film" + Dot + Runtime(minutes) : "Film",
        PlaylistEntryKind.Show => title?.SeasonCount is { } seasons and > 0
            ? "Show" + Dot + seasons + (seasons == 1 ? " season" : " seasons")
            : "Show",
        PlaylistEntryKind.Game => "Game",
        PlaylistEntryKind.Franchise => "Film series",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A playlist entry kind with no words decided."),
    };

    /// <summary>"1h 44m", "2h 06m", "52m" — the minutes always two digits once there is an hour.</summary>
    private static string Runtime(int minutes) =>
        minutes < 60
            ? $"{minutes}m"
            : string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}h {minutes % 60:00}m");

    private static (string? Line, bool Emphasis) Where(CatalogueTitle title, IReadOnlyList<ServicePlan> plans, HomeOverrides overrides)
    {
        var choice = BestHome.Choose(title.Id, TitleOffers.Streams(title.Offers), plans, overrides);

        if (choice.Best is { } best)
        {
            var offer = title.Offers
                .Where(o => o.Service == best.Service && o.Kind is OfferKind.Subscription or OfferKind.Free)
                .OrderByDescending(o => o.Quality)
                .FirstOrDefault();
            var serviceName = offer?.ServiceName ?? best.Service.Value;

            var leaving = title.Offers
                .Where(o => o.Service == best.Service && o.Kind is OfferKind.Subscription or OfferKind.Free)
                .Where(o => o.ExpiresSoon && o.ExpiresOn is not null)
                .Select(o => o.ExpiresOn!.Value)
                .OrderBy(date => date)
                .Cast<DateOnly?>()
                .FirstOrDefault();
            if (leaving is { } leaves)
            {
                return ("Leaves " + serviceName + " " + Day(leaves), false);
            }

            var line = "On " + serviceName;
            if (offer?.Kind == OfferKind.Free)
            {
                line += Dot + "free, with ads";
            }
            else if (best.WithAds)
            {
                line += Dot + Quality(best.Quality) + ", with ads";
                return (line, false);
            }

            if (best.Quality == VideoQuality.Uhd)
            {
                line += Dot + "4K";
            }

            return (line, false);
        }

        const string notOurs = "Not on your services";
        var paid = RentLine.Cheapest(TitleOffers.Paid(title.Offers));
        if (paid is null)
        {
            return (notOurs, true);
        }

        var store = title.Offers.FirstOrDefault(o => o.Service == paid.Store)?.ServiceName ?? paid.Store.Value;
        var verb = paid.Kind == PaidKind.Rent ? "rent from " : "buy from ";
        var price = paid.Price.ToString("0.00", CultureInfo.InvariantCulture);
        return (notOurs + Dot + verb + store + ", $" + price + " " + Quality(paid.Quality), true);
    }

    private static string Quality(VideoQuality quality) => quality switch
    {
        VideoQuality.Uhd => "4K",
        VideoQuality.Hd => "HD",
        VideoQuality.Sd => "SD",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "A picture quality with no label."),
    };

    private static string Day(DateOnly date) => date.ToString("MMM d", CultureInfo.InvariantCulture);
}
