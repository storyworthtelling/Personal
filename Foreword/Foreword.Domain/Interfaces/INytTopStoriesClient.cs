using Foreword.Domain.Models;

namespace Foreword.Domain.Interfaces;

public interface INytTopStoriesClient
{
    Task<IReadOnlyList<TopStory>> GetTopNewsStories(int count);
    Task<IReadOnlyList<TopStory>> GetTopOpinionArticles(int count);
}
