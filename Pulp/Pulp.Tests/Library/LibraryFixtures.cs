using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Tests.Library;

/// <summary>Invented titles, services and a fixed clock shared by the Library tests.</summary>
internal static class LibraryFixtures
{
    public static readonly DateTimeOffset Now = new(2026, 3, 12, 21, 0, 0, TimeSpan.Zero);

    public static readonly ServiceId Streamly = new("streamly-example");

    public static readonly TitleId Lighthouse = new("lighthouse-show-example");
    public static readonly TitleId Ferry = new("ferry-show-example");
    public static readonly TitleId Larkspur = new("larkspur-film-example");
    public static readonly TitleId Tidewater = new("tidewater-film-example");
    public static readonly TitleId Pennant = new("pennant-game-example");

    public static EpisodeRef E(int season, int number) => new(season, number);

    public static DateTimeOffset DaysAgo(double days) => Now.AddDays(-days);

    public static WatchEvent Episode(TitleId show, EpisodeRef episode, DateTimeOffset at) =>
        new(show, WatchedKind.Show, at, Streamly, episode);

    public static WatchEvent PartEpisode(TitleId show, EpisodeRef episode, DateTimeOffset at) =>
        new(show, WatchedKind.Show, at, Streamly, episode, StoppedAt: TimeSpan.FromMinutes(10), Runtime: TimeSpan.FromMinutes(50));

    public static WatchEvent FilmFinished(TitleId film, DateTimeOffset at) =>
        new(film, WatchedKind.Film, at, Streamly);

    public static WatchEvent FilmStopped(TitleId film, DateTimeOffset at, int minute, int runtime) =>
        new(film, WatchedKind.Film, at, Streamly, StoppedAt: TimeSpan.FromMinutes(minute), Runtime: TimeSpan.FromMinutes(runtime));

    /// <summary>A season of <paramref name="count"/> episodes, one a week, the first airing at <paramref name="first"/>.</summary>
    public static IEnumerable<ScheduledEpisode> Season(int season, int count, DateTimeOffset first) =>
        Enumerable.Range(1, count).Select(n => new ScheduledEpisode(new EpisodeRef(season, n), first.AddDays(7 * (n - 1))));
}
