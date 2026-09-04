using System.Text.Json;

namespace Codex.Infrastructure.Podcasts;

/// <summary>One show a search turned up, and the feed that makes it subscribable.</summary>
public sealed record PodcastSearchResult(string Title, string Author, string FeedUrl, string? ArtworkUrl = null);

/// <summary>
/// Finding a podcast you do not already follow. Kept an interface so the area can be
/// built and tested without the network.
/// </summary>
public interface IPodcastSearch
{
    Task<IReadOnlyList<PodcastSearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
}

/// <summary>
/// Searches Apple's public podcast directory — the one keyless, account-free index of
/// the open podcast web, and the only part of Discover's search that does not wait on
/// a streaming provider. It returns the show's own RSS feed, which is what Score
/// actually subscribes to, so nothing about a subscription depends on Apple after the
/// search that found it.
///
/// Every failure is empty: a directory that is down, throttled, or answering in a
/// shape we do not recognize means "no results", never an exception into a search
/// field someone is typing in.
/// </summary>
public sealed class PodcastSearchClient : IPodcastSearch
{
    private const string SearchUrl = "https://itunes.apple.com/search?media=podcast&limit=25&term=";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient httpClient;

    public PodcastSearchClient(HttpClient httpClient) => this.httpClient = httpClient;

    public async Task<IReadOnlyList<PodcastSearchResult>> SearchAsync(
        string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var url = SearchUrl + Uri.EscapeDataString(query.Trim());
            var json = await httpClient.GetStringAsync(url, cancellationToken);
            var reply = JsonSerializer.Deserialize<SearchReply>(json, SerializerOptions);

            return [.. (reply?.Results ?? [])
                .Where(result => !string.IsNullOrWhiteSpace(result.FeedUrl))
                .Select(result => new PodcastSearchResult(
                    result.CollectionName ?? "Untitled show",
                    result.ArtistName ?? string.Empty,
                    result.FeedUrl!,
                    result.ArtworkUrl600))];
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            return [];
        }
    }

    private sealed record SearchReply(List<SearchResultDto>? Results);

    private sealed record SearchResultDto(
        string? CollectionName, string? ArtistName, string? FeedUrl, string? ArtworkUrl600);
}
