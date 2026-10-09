using Pulp.Domain.Core;

namespace Pulp.Domain.Library;

/// <summary>An episode and when it airs (or aired).</summary>
public sealed record ScheduledEpisode(EpisodeRef Episode, DateTimeOffset AirsAt);

/// <summary>A show's known episodes, aired and announced, and whether the show has ended.</summary>
public sealed record ShowSchedule(TitleId Title, IReadOnlyList<ScheduledEpisode> Episodes, bool Ended);
