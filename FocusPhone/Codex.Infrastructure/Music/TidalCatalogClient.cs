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

            var artistName = string.Join(", ", (resource.Relationships?.Artists?.Data ?? [])
                .Select(artist => artistsById.TryGetValue(artist.Id, out var found) ? found.Attributes?.Name : null)
                .Where(name => !string.IsNullOrWhiteSpace(name)));

            var albumId = resource.Relationships?.Albums?.Data?.FirstOrDefault()?.Id;
            var albumName = albumId is not null && albumsById.TryGetValue(albumId, out var album)
                ? album.Attributes?.Title ?? string.Empty
                : string.Empty;

            tracks.Add(new Track(
                Id: $"tidal:{resource.Id}",
                Title: resource.Attributes.Title ?? string.Empty,
                ArtistName: artistName,
                AlbumName: albumName,
                AlbumArtist: artistName,
                TrackNumber: 0,
                DiscNumber: 0,
                Genre: string.Empty,
                Duration: ParseDuration(resource.Attributes.Duration),
                Availability: TrackAvailability.Streaming));
        }

        return tracks;
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

    private sealed record SearchResultData(RelationshipSet? Relationships = null);

    // Every field here is optional because JSON:API only sends the relationships a
    // resource actually has — a track with one artist and no album carries no
    // "albums" key at all, rather than an empty one.
    private sealed record RelationshipSet(
        RelationshipList? Tracks = null, RelationshipList? Albums = null, RelationshipList? Artists = null);

    private sealed record RelationshipList(List<ResourceRef>? Data = null);

    private sealed record IncludedResource(
        string Id, string Type, ResourceAttributes? Attributes = null, RelationshipSet? Relationships = null);

    // Shared across tracks/albums/artists: only the fields the type in question
    // actually carries will be non-null after deserializing.
    private sealed record ResourceAttributes(string? Title = null, string? Name = null, string? Duration = null);
}
