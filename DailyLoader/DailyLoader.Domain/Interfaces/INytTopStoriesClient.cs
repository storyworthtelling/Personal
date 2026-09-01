using DailyLoader.Domain.Models;

namespace DailyLoader.Domain.Interfaces;

public interface INytTopStoriesClient
{
    Task<IReadOnlyList<TopStory>> GetTopNewsStories(int count);
    Task<IReadOnlyList<TopStory>> GetTopOpinionArticles(int count);
}
