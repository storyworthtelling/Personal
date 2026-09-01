using System.Net.Http.Headers;
using System.Text.Json;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>
/// TIDAL's catalogue, over <c>openapi.tidal.com/v2</c> directly rather than the bound
/// player module — the catalogue is a plain JSON:API REST surface, and a fake
/// <see cref="HttpClient"/> can stand in for it in a way a Kotlin binding never could.
///
/// Search is a two-step JSON:API flow, confirmed against a real call rather than
/// assumed from documentation: filtering by the query string returns an opaque
/// <c>searchResults</c> resource id, and fetching <i>that</i> resource with
/// <c>include=</c> is what actually returns tracks. Treating the query itself as the
/// resource id — the natural first guess — is rejected by the service.
/// </summary>
public sealed class TidalCatalogClient(HttpClient httpClient)
{
    private const string BaseUrl = "https://openapi.tidal.com/v2/";
    private const string Include = "tracks,tracks.artists,tracks.albums";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Searches TIDAL's catalogue. Every failure — no match, a rejected
    /// token, the service being down — comes back empty, the same rule the other
    /// keyless clients follow: a search field someone is typing in must never surface
    /// an exception.</summary>
    public async Task<IReadOnlyList<Track>> SearchAsync(
        string accessToken, string query, string countryCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var searchUrl =
                $"{BaseUrl}searchResults?filter%5Bquery%5D={Uri.EscapeDataString(query.Trim())}" +
                $"&countryCode={Uri.EscapeDataString(countryCode)}";
            var searchReply = await Get<SearchIdReply>(accessToken, searchUrl, cancellationToken);
            var resultId = searchReply?.Data?.FirstOrDefault()?.Id;
            if (string.IsNullOrEmpty(resultId))
            {
                return [];
            }

            var resultUrl =
                $"{BaseUrl}searchResults/{Uri.EscapeDataString(resultId)}" +
                $"?countryCode={Uri.EscapeDataString(countryCode)}&include={Uri.EscapeDataString(Include)}";
            var reply = await Get<SearchResultReply>(accessToken, resultUrl, cancellationToken);
            return BuildTracks(reply);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Looks up one track directly by its TIDAL id — for re-checking whether a
    /// previously-saved track is still available, not for search. A single-resource
    /// JSON:API GET rather than the two-step dance <see cref="SearchAsync"/> needs:
    /// there is no opaque intermediate resource to resolve first when the id is
    /// already known. Not found, a rejected token, or the service being down all
    /// come back null, the same "never surface an exception" rule as
    /// <see cref="SearchAsync"/> — a background availability check must never crash
    /// the catalog load it feeds.
    /// </summary>
    public async Task<Track?> GetTrackAsync(
        string accessToken, string trackId, string countryCode, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{BaseUrl}tracks/{Uri.EscapeDataString(trackId)}" +
                $"?countryCode={Uri.EscapeDataString(countryCode)}&include=artists%2Calbums";
            var reply = await Get<TrackResourceReply>(accessToken, url, cancellationToken);
            return reply?.Data is { Attributes: not null } resource
                ? BuildTrack(resource, reply.Included ?? [])
                : null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    // A file at or above this width is judged plenty for anywhere Score shows a
    // cover — a phone list row or the now-playing bar, never a full-bleed print —
    // so there is no reason to fetch the largest (and slowest) file TIDAL offers.
    private const int PreferredArtworkPx = 320;

    /// <summary>
    /// The best-sized cover art URL for a track, or null if the track has no album
    /// or the album has no cover art. Two requests, not one: cover art is not a
    /// track or album attribute — TIDAL's published OpenAPI spec shows
    /// Albums_Attributes carries no image field at all — it is a separate
    /// <c>coverArt</c> relationship resolving to an <c>artworks</c> resource with a
    /// <c>files[]</c> of different pixel sizes, and an album's id is not known until
    /// the track itself has been fetched. Meant for one-off use (saving a track),
    /// not for a whole page of search results, which is why this is not folded into
    /// <see cref="SearchAsync"/> — N extra round trips for N results nobody has
    /// chosen yet would make search itself slower for no benefit.
    /// </summary>
    public async Task<string?> GetTrackArtworkUrlAsync(
        string accessToken, string trackId, string countryCode, CancellationToken cancellationToken)
    {
        try
        {
            var trackUrl = $"{BaseUrl}tracks/{Uri.EscapeDataString(trackId)}" +
                $"?countryCode={Uri.EscapeDataString(countryCode)}&include=albums";
            var trackReply = await Get<TrackResourceReply>(accessToken, trackUrl, cancellationToken);
            var albumId = trackReply?.Data?.Relationships?.Albums?.Data?.FirstOrDefault()?.Id;
            if (string.IsNullOrEmpty(albumId))
            {
                return null;
            }

            var albumUrl = $"{BaseUrl}albums/{Uri.EscapeDataString(albumId)}" +
                $"?countryCode={Uri.EscapeDataString(countryCode)}&include=coverArt";
            var albumReply = await Get<TrackResourceReply>(accessToken, albumUrl, cancellationToken);
            var files = albumReply?.Included?.FirstOrDefault(r => r.Type == "artworks")?.Attributes?.Files;
            if (files is not { Count: > 0 })
            {
                return null;
            }

            // Smallest file that still clears the preferred size; if none does (every
            // file is smaller than preferred), the largest available is the best on offer.
            return files
                .Where(f => (f.Meta?.Width ?? 0) >= PreferredArtworkPx)
                .OrderBy(f => f.Meta!.Width)
                .Select(f => f.Href)
                .FirstOrDefault()
                ?? files.OrderByDescending(f => f.Meta?.Width ?? 0).First().Href;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    // The relationship list gives the order and which tracks belong to this result;
    // "included" is an unordered pool of every tracks/albums/artists resource the
    // include= asked for, matched back to it by (id, type) — JSON:API's normal shape.
    private static IReadOnlyList<Track> BuildTracks(SearchResultReply? reply)
    {
        var trackRefs = reply?.Data?.Relationships?.Tracks?.Data ?? [];
        var included = reply?.Included ?? [];
        var tracksById = included.Where(r => r.Type == "tracks").ToDictionary(r => r.Id, r => r);
        var albumsById = included.Where(r => r.Type == "albums").ToDictionary(r => r.Id, r => r);
        var artistsById = included.Where(r => r.Type == "artists").ToDictionary(r => r.Id, r => r);

        var tracks = new List<Track>();
        foreach (var reference in trackRefs)
        {
            // TIDAL's own pagination can cut "included" off before every id the
            // relationship names is resolved. Skip rather than fabricate a blank track.
            if (!tracksById.TryGetValue(reference.Id, out var resource) || resource.Attributes is null)
            {
                continue;
            }

            tracks.Add(BuildTrack(resource, included, artistsById, albumsById));
        }

        return tracks;
    }

    // Shared by SearchAsync (a track resource found among "included") and
    // GetTrackAsync (a track resource that IS "data") — same resource shape either
    // way, so the same lookup-and-build logic applies once the id maps are ready.
    private static Track BuildTrack(
        IncludedResource resource, IReadOnlyList<IncludedResource> included,
        Dictionary<string, IncludedResource>? artistsById = null, Dictionary<string, IncludedResource>? albumsById = null)
    {
        artistsById ??= included.Where(r => r.Type == "artists").ToDictionary(r => r.Id, r => r);
        albumsById ??= included.Where(r => r.Type == "albums").ToDictionary(r => r.Id, r => r);

        var artistName = string.Join(", ", (resource.Relationships?.Artists?.Data ?? [])
            .Select(artist => artistsById.TryGetValue(artist.Id, out var found) ? found.Attributes?.Name : null)
            .Where(name => !string.IsNullOrWhiteSpace(name)));

        var albumId = resource.Relationships?.Albums?.Data?.FirstOrDefault()?.Id;
        var albumName = albumId is not null && albumsById.TryGetValue(albumId, out var album)
            ? album.Attributes?.Title ?? string.Empty
            : string.Empty;

        return new Track(
            Id: $"tidal:{resource.Id}",
            Title: resource.Attributes?.Title ?? string.Empty,
            ArtistName: artistName,
            AlbumName: albumName,
            AlbumArtist: artistName,
            TrackNumber: 0,
            DiscNumber: 0,
            Genre: string.Empty,
            Duration: ParseDuration(resource.Attributes?.Duration),
            Availability: TrackAvailability.Streaming);
    }

    // TIDAL reports duration as an ISO 8601 duration ("PT2M17S"), which is exactly the
    // subset System.Xml's duration parsing already understands — no hand-rolled parser.
    private static TimeSpan ParseDuration(string? iso8601) =>
        string.IsNullOrEmpty(iso8601) ? TimeSpan.Zero : System.Xml.XmlConvert.ToTimeSpan(iso8601);

    private async Task<T?> Get<T>(string accessToken, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }

    private sealed record SearchIdReply(List<ResourceRef>? Data = null);

    private sealed record ResourceRef(string Id, string Type);

    private sealed record SearchResultReply(SearchResultData? Data = null, List<IncludedResource>? Included = null);

    // GetTrackAsync's envelope: Data IS the track resource directly (not a wrapper
    // with a "tracks" relationship, since the id is already known), reusing
    // IncludedResource's shape rather than a new type for the same fields.
    private sealed record TrackResourceReply(IncludedResource? Data = null, List<IncludedResource>? Included = null);

    private sealed record SearchResultData(RelationshipSet? Relationships = null);

    // Every field here is optional because JSON:API only sends the relationships a
    // resource actually has — a track with one artist and no album carries no
    // "albums" key at all, rather than an empty one.
    private sealed record RelationshipSet(
        RelationshipList? Tracks = null, RelationshipList? Albums = null, RelationshipList? Artists = null);

    private sealed record RelationshipList(List<ResourceRef>? Data = null);

    private sealed record IncludedResource(
        string Id, string Type, ResourceAttributes? Attributes = null, RelationshipSet? Relationships = null);

    // Shared across tracks/albums/artists/artworks: only the fields the type in
    // question actually carries will be non-null after deserializing.
    private sealed record ResourceAttributes(
        string? Title = null, string? Name = null, string? Duration = null, List<ArtworkFile>? Files = null);

    private sealed record ArtworkFile(string? Href = null, ArtworkFileMeta? Meta = null);

    private sealed record ArtworkFileMeta(int? Width = null, int? Height = null);
}
