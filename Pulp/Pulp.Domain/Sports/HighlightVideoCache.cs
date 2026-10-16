using System.Collections.Concurrent;

namespace Pulp.Domain.Sports;

/// <summary>
/// The highlight videos found this session, by game. Only a find is kept: "none yet" is the answer a game
/// gets in the first hours after the final whistle and must not stick for the rest of the session, so a
/// miss is asked again next tap. Safe to use from the lookup's background task and the UI thread at once.
/// </summary>
public sealed class HighlightVideoCache
{
    private readonly ConcurrentDictionary<GameId, string> found = new();

    public bool TryGet(GameId game, out string video) => found.TryGetValue(game, out video!);

    /// <summary>Keeps <paramref name="video"/> when there is one; a null or blank one is not remembered.</summary>
    public void Remember(GameId game, string? video)
    {
        if (!string.IsNullOrWhiteSpace(video))
        {
            found[game] = video;
        }
    }
}
