using NytRemarkable.Domain.Models;

namespace NytRemarkable.Domain.Interfaces;

public interface INytTopStoriesClient
{
    Task<IReadOnlyList<TopStory>> GetTopNewsStories(int count);
    Task<IReadOnlyList<TopStory>> GetTopOpinionArticles(int count);
}
