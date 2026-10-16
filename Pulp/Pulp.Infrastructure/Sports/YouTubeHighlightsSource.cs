using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Infrastructure.Sports;

/// <summary>
/// <see cref="IHighlightsSource"/> over the YouTube Data API, through the same client and key the trailers use
/// (D313). One search inside the league's official channel, from the game's day to fourteen days after (D349), for
/// "away home highlights". The id is checked to be a YouTube video id before it leaves; anything else is "none".
/// </summary>
public sealed class YouTubeHighlightsSource : IHighlightsSource
{
    private const int VideoIdLength = 11;
    private static readonly TimeSpan Window = TimeSpan.FromDays(14);

    private readonly YouTubeClient client;

    public YouTubeHighlightsSource(YouTubeClient client) =>
        this.client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<string?> FindVideoIdAsync(HighlightsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dayStart = new DateTimeOffset(request.PlayedOn.UtcDateTime.Date, TimeSpan.Zero);
        var id = await client.FindInChannelAsync(
            request.ChannelId, request.Away + " " + request.Home + " highlights", dayStart, dayStart + Window, cancellationToken);
        return IsVideoId(id) ? id : null;
    }

    private static bool IsVideoId(string? candidate) =>
        candidate is { Length: VideoIdLength } && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
