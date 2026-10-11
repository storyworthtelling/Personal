using System.Globalization;
using System.Net;
using System.Text.Json;
using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Services;
using Storyworthtelling.Voice.Health;

namespace Pulp.Infrastructure.Catalogue;

/// <summary>Which host the Streaming Availability key was issued for; each takes the key in its own header.</summary>
public enum StreamingAvailabilityRoute
{
    /// <summary>A key from developers.movieofthenight.com: <c>api.movieofthenight.com/v4</c>, header <c>X-API-Key</c>.</summary>
    Direct,

    /// <summary>A key from RapidAPI: <c>streaming-availability.p.rapidapi.com</c>, header <c>X-RapidAPI-Key</c>.</summary>
    RapidApi,
}

/// <summary>
/// The availability source (D203: Streaming Availability by Movie of the Night, never TMDB): a title by
/// id, a title search, and the changes feed for the household's services (D204: weekly refresh, the
/// daily changes feed, a fresh look on open). Every shape here is the v4 OpenAPI document's —
/// https://github.com/movieofthenight/streaming-availability-api/blob/main/openapi.yaml — paths
/// <c>/shows/{id}</c>, <c>/shows/search/title</c>, <c>/changes</c>; schemas <c>show</c>,
/// <c>streamingOption</c>, <c>season</c>, <c>episode</c>, <c>change</c>, <c>changesResult</c>.
/// The key is read live on every call and sent in a header, never the URL.
/// </summary>
public sealed class StreamingAvailabilityClient
{
    private readonly IPulpKeySource keys;
    private readonly ServiceCatalogue catalogue;
    private readonly ProviderHttp http;
    private readonly StreamingAvailabilityRoute route;
    private readonly string country;

    public StreamingAvailabilityClient(
        HttpClient httpClient,
        IPulpKeySource keys,
        ServiceCatalogue catalogue,
        ILookupCounter? counter = null,
        ICredentialHealthSink? healthSink = null,
        StreamingAvailabilityRoute route = StreamingAvailabilityRoute.Direct,
        string country = "us")
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentException.ThrowIfNullOrWhiteSpace(country);
        this.keys = keys;
        this.catalogue = catalogue;
        this.route = route;
        this.country = country.Trim().ToLowerInvariant();
        http = new ProviderHttp(httpClient, LookupSource.StreamingAvailability, PulpCredentialIds.StreamingAvailability, counter, healthSink);
    }

    /// <summary>
    /// One title by the source's id or its IMDb id, with seasons and episodes (series_granularity
    /// episode, the default, which is what carries per-episode deep links). Null when the source does
    /// not know the id (its documented 404).
    /// </summary>
    public async Task<CatalogueTitle?> GetTitleAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var what = "a title lookup";
        var response = await http.GetAsync(
            Endpoint("/shows/" + Uri.EscapeDataString(id.Trim()), ("country", country), ("series_granularity", "episode")),
            Headers(), what, cancellationToken, HttpStatusCode.NotFound);
        if (response.Status == HttpStatusCode.NotFound)
        {
            return null;
        }

        using var document = http.Parse(response, what);
        return Title(document.RootElement, what);
    }

    /// <summary>Titles matching a phrase (D209: search is by title), without seasons, narrowed to films or shows when given.</summary>
    public async Task<IReadOnlyList<CatalogueTitle>> SearchByTitleAsync(string title, TitleKind? kind, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var what = "a title search";
        var query = new List<(string, string)> { ("country", country), ("title", title.Trim()), ("series_granularity", "show") };
        if (kind is not null)
        {
            query.Add(("show_type", ShowType(kind.Value)));
        }

        var response = await http.GetAsync(Endpoint("/shows/search/title", [.. query]), Headers(), what, cancellationToken);
        using var document = http.Parse(response, what);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw http.Malformed(what, "expected an array of shows");
        }

        return document.RootElement.EnumerateArray().Select(show => Title(show, what)).ToList();
    }

    /// <summary>
    /// One page of changes of one kind on the household's services, by their source ids. Expiring and
    /// upcoming changes include those with no announced date, so "leaving soon" with no day still
    /// arrives (D124). Pass the previous page's <see cref="ChangesPage.NextCursor"/> to continue.
    /// </summary>
    public async Task<ChangesPage> GetChangesAsync(
        IEnumerable<StreamingService> services,
        ChangeKind kind,
        ChangeItem item,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        var catalogs = services.Select(service => service.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (catalogs.Count == 0)
        {
            throw new ArgumentException("The changes feed needs at least one of the household's services; with none it would report every service.", nameof(services));
        }

        var query = new List<(string, string)>
        {
            ("country", country),
            ("catalogs", string.Join(',', catalogs)),
            ("change_type", ChangeKindName(kind)),
            ("item_type", ItemName(item)),
            ("include_unknown_dates", "true"),
        };
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query.Add(("cursor", cursor));
        }

        var what = "a changes request";
        var response = await http.GetAsync(Endpoint("/changes", [.. query]), Headers(), what, cancellationToken);
        using var document = http.Parse(response, what);
        var root = document.RootElement;
        if (root.Child("changes").ValueKind != JsonValueKind.Array)
        {
            throw http.Malformed(what, "no changes array");
        }

        var titles = new Dictionary<TitleId, CatalogueTitle>();
        var shows = root.Child("shows");
        if (shows.ValueKind == JsonValueKind.Object)
        {
            foreach (var show in shows.EnumerateObject())
            {
                var mapped = Title(show.Value, what);
                titles[mapped.Id] = mapped;
            }
        }

        var changes = root.Child("changes").Items().Select(change => Change(change, what)).ToList();
        var next = root.Flag("hasMore") ? root.Text("nextCursor") : null;
        return new ChangesPage(changes, titles, string.IsNullOrWhiteSpace(next) ? null : next);
    }

    private CatalogueTitle Title(JsonElement show, string what)
    {
        var id = show.Text("id");
        var name = show.Text("title");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            throw http.Malformed(what, "a show without an id or a title");
        }

        var kind = show.Text("showType") switch
        {
            "movie" => TitleKind.Film,
            "series" => TitleKind.Show,
            var other => throw http.Malformed(what, $"show type '{other}'"),
        };

        return new CatalogueTitle
        {
            Id = new TitleId(id),
            Kind = kind,
            Name = name,
            ImdbId = show.Text("imdbId"),
            Year = kind == TitleKind.Film ? show.Number("releaseYear") : show.Number("firstAirYear"),
            Genres = show.Child("genres").Items().Select(genre => genre.Text("name")).OfType<string>().ToList(),
            Synopsis = show.Text("overview") ?? string.Empty,

            // The vertical poster at 480 wide: the phone's poster window, without paying for 720.
            PosterUrl = show.Child("imageSet").Child("verticalPoster").Text("w480"),
            RuntimeMinutes = show.Number("runtime"),
            SeasonCount = show.Number("seasonCount"),
            EpisodeCount = show.Number("episodeCount"),
            Offers = Offers(show),
            Seasons = show.Child("seasons").Items().Select((season, index) => Season(season, index + 1)).ToList(),
        };
    }

    /// <summary>
    /// The source gives seasons and episodes in order and numbers neither; their place in the list is
    /// the number, matching the season/episode numbers its changes feed reports.
    /// </summary>
    private CatalogueSeason Season(JsonElement season, int number) =>
        new(
            number,
            season.Text("title") ?? "Season " + number.ToString(CultureInfo.InvariantCulture),
            season.Child("episodes").Items()
                .Select((episode, index) => new CatalogueEpisode(
                    new EpisodeRef(number, index + 1),
                    episode.Text("title") ?? string.Empty,
                    episode.Text("overview") ?? string.Empty,
                    episode.Number("airYear"),
                    null,
                    null,
                    Offers(episode)))
                .ToList());

    /// <summary>The <c>streamingOptions</c> map's entry for the household's country.</summary>
    private List<TitleOffer> Offers(JsonElement item) =>
        item.Child("streamingOptions").Child(country).Items().Select(Offer).OfType<TitleOffer>().ToList();

    private TitleOffer? Offer(JsonElement option)
    {
        var sourceId = option.Child("service").Text("id");
        var link = option.Text("link");
        var kind = OfferKindOf(option.Text("type"));
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(link) || kind is null)
        {
            // An option missing a required field, or of a type this build does not know, is dropped
            // rather than guessed at: a wrong kind would open a title somewhere it does not play.
            return null;
        }

        var (service, known) = Service(sourceId);
        var price = option.Child("price");
        return new TitleOffer(
            service,
            option.Child("service").Text("name") ?? sourceId,
            known,
            kind.Value,
            Quality(option.Text("quality")),
            Amount(price.Text("amount")),
            price.Text("currency"),
            link,
            option.Text("videoLink"),
            option.Long("expiresOn") is { } expires ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime) : null,
            option.Flag("expiresSoon"));
    }

    private CatalogueChange Change(JsonElement change, string what)
    {
        var showId = change.Text("showId");
        var sourceId = change.Child("service").Text("id");
        var offer = OfferKindOf(change.Text("streamingOptionType"));
        if (string.IsNullOrWhiteSpace(showId) || string.IsNullOrWhiteSpace(sourceId) || offer is null)
        {
            throw http.Malformed(what, "a change without a show, a service or an option type");
        }

        return new CatalogueChange(
            change.Text("changeType") switch
            {
                "new" => ChangeKind.New,
                "removed" => ChangeKind.Removed,
                "updated" => ChangeKind.Updated,
                "expiring" => ChangeKind.Expiring,
                "upcoming" => ChangeKind.Upcoming,
                var other => throw http.Malformed(what, $"change type '{other}'"),
            },
            change.Text("itemType") switch
            {
                "show" => ChangeItem.Title,
                "season" => ChangeItem.Season,
                "episode" => ChangeItem.Episode,
                var other => throw http.Malformed(what, $"item type '{other}'"),
            },
            new TitleId(showId),
            change.Text("showType") == "series" ? TitleKind.Show : TitleKind.Film,
            Service(sourceId).Service,
            offer.Value,
            change.Number("season"),
            change.Number("episode"),
            change.Long("timestamp") is { } at ? DateTimeOffset.FromUnixTimeSeconds(at) : null,
            change.Text("link"));
    }

    /// <summary>The catalogue's id for a service it knows; the source's own id for one it does not (a store, say).</summary>
    private (ServiceId Service, bool Known) Service(string sourceId) =>
        catalogue.BySourceId(sourceId) is { } known ? (known.Id, true) : (new ServiceId(sourceId), false);

    private static OfferKind? OfferKindOf(string? type) => type switch
    {
        "subscription" => OfferKind.Subscription,
        "free" => OfferKind.Free,
        "addon" => OfferKind.Addon,
        "rent" => OfferKind.Rent,
        "buy" => OfferKind.Buy,
        _ => null,
    };

    /// <summary>
    /// The source's four qualities onto Pulp's three. Quad-HD reads as HD, not ultra-HD, so a 1440p
    /// stream never outranks a true 4K one in the best-home choice (D28); an absent quality reads as SD.
    /// </summary>
    private static VideoQuality Quality(string? quality) => quality switch
    {
        "uhd" => VideoQuality.Uhd,
        "qhd" or "hd" => VideoQuality.Hd,
        _ => VideoQuality.Sd,
    };

    /// <summary>The price's <c>amount</c>, a decimal written as a string by the source.</summary>
    private static decimal? Amount(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : null;

    private static string ShowType(TitleKind kind) => kind == TitleKind.Film ? "movie" : "series";

    private static string ChangeKindName(ChangeKind kind) => kind switch
    {
        ChangeKind.New => "new",
        ChangeKind.Removed => "removed",
        ChangeKind.Updated => "updated",
        ChangeKind.Expiring => "expiring",
        _ => "upcoming",
    };

    private static string ItemName(ChangeItem item) => item switch
    {
        ChangeItem.Title => "show",
        ChangeItem.Season => "season",
        _ => "episode",
    };

    private Uri Endpoint(string path, params (string Name, string Value)[] query)
    {
        var root = route == StreamingAvailabilityRoute.Direct
            ? "https://api.movieofthenight.com/v4"
            : "https://streaming-availability.p.rapidapi.com";
        return new Uri(root + path + "?" + string.Join('&', query.Select(pair => pair.Name + "=" + Uri.EscapeDataString(pair.Value))));
    }

    /// <summary>
    /// The key header named by the OpenAPI document's security schemes for the chosen host. On the
    /// RapidAPI route the host header is RapidAPI's own convention for every API it fronts — not in
    /// this document — and is sent as well.
    /// </summary>
    private Dictionary<string, string> Headers()
    {
        var key = http.RequireKey(keys.Keys.StreamingAvailabilityKey, "Streaming Availability");
        return route == StreamingAvailabilityRoute.Direct
            ? new() { ["X-API-Key"] = key }
            : new() { ["X-RapidAPI-Key"] = key, ["X-RapidAPI-Host"] = "streaming-availability.p.rapidapi.com" };
    }
}
