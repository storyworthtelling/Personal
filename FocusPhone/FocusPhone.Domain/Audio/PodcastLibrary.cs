namespace FocusPhone.Domain.Audio;

/// <summary>
/// Tracks listened state over the subscribed feeds' episodes. The default
/// view is the user's queue: unlistened episodes, newest first.
/// </summary>
public class PodcastLibrary
{
    private readonly List<Episode> episodes;
    private readonly HashSet<string> listenedIds;

    public PodcastLibrary(IEnumerable<Episode> episodes, IEnumerable<string>? listenedIds = null)
    {
        this.episodes = [.. episodes];
        this.listenedIds = listenedIds is null ? [] : [.. listenedIds];
    }

    public void MarkListened(string episodeId) => listenedIds.Add(episodeId);

    public void MarkUnlistened(string episodeId) => listenedIds.Remove(episodeId);

    public IReadOnlyList<Episode> GetDefaultView() =>
        episodes
            .Where(episode => !listenedIds.Contains(episode.Id))
            .OrderByDescending(episode => episode.PublishedUtc)
            .ToList();

    public IReadOnlyList<Episode> GetAllEpisodes() =>
        episodes.OrderByDescending(episode => episode.PublishedUtc).ToList();

    public IReadOnlyCollection<string> ExportListenedIds() => [.. listenedIds];
}
