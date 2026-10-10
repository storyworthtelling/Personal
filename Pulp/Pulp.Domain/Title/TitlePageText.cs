using System.Globalization;
using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Domain.Title;

/// <summary>
/// The title page's words (mockup 02, D36, D158, D216; a film's page, D272), worked out here so the
/// head only lays them out. No cast and no scores (D36), and no rating reasons (D207): the chip
/// carries the rating on its own.
/// </summary>
public static class TitlePageText
{
    /// <summary>The link under the best home that makes the other home this title's home (D216).</summary>
    public const string OpenThereInstead = "open there instead";

    /// <summary>The quiet button by the poster (D34, D158); it never autoplays (D77).</summary>
    public const string TrailerLabel = "Trailer";

    /// <summary>The page's status sentence while the title is being looked up (§6: a sentence, not a spinner).</summary>
    public const string Looking = "Looking this title up…";

    /// <summary>Neither the cache nor the source could say anything about the title.</summary>
    public const string NotFound = "This title could not be looked up, and no copy of it is kept on this phone. Try again later.";

    /// <summary>Play with nobody on the household roster yet: the child gate has nobody to judge for.</summary>
    public const string NoHousehold = "Play waits until the household is set up in Settings.";

    /// <summary>The household could not be read into a roster the child gate can judge by; the reason is in Diagnostics.</summary>
    public const string HouseholdUnreadable = "Play waits until the household's settings can be read. Diagnostics has the reason.";

    /// <summary>None of the household's services streams the title.</summary>
    public const string NotOnOurServices = "None of your services streams this title.";

    /// <summary>The home carries the title but gave no link Pulp can hand off.</summary>
    public const string NoLink = "There is no link to open this title on its service yet.";

    private const string Separator = " · ";

    /// <summary>
    /// The line under the poster: the first genre, then a show's seasons and episodes, or a film's
    /// runtime (D36, D272). Whatever the source did not say is left out, never guessed.
    /// </summary>
    public static string KindLine(CatalogueTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var parts = new List<string>();
        if (title.Genres.FirstOrDefault(genre => !string.IsNullOrWhiteSpace(genre)) is { } genre)
        {
            parts.Add(genre.Trim());
        }

        if (title.Kind == TitleKind.Show)
        {
            var seasons = title.SeasonCount ?? (title.Seasons.Count > 0 ? title.Seasons.Count : null);
            if (seasons is { } seasonCount)
            {
                parts.Add(Count(seasonCount, "season"));
            }

            var episodes = title.EpisodeCount ?? (title.Seasons.Count > 0 ? title.Seasons.Sum(season => season.Episodes.Count) : null);
            if (episodes is { } episodeCount)
            {
                parts.Add(Count(episodeCount, "episode"));
            }
        }
        else if (title.RuntimeMinutes is > 0 and var minutes)
        {
            parts.Add(Runtime(minutes));
        }

        return string.Join(Separator, parts);
    }

    /// <summary>A film's length as hours and minutes: "1h 52m", "45m", "2h".</summary>
    public static string Runtime(int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minutes);
        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours == 0)
        {
            return Invariant($"{rest}m");
        }

        return rest == 0 ? Invariant($"{hours}h") : Invariant($"{hours}h {rest}m");
    }

    /// <summary>An episode as the bar and the card name it: "S1 · E3".</summary>
    public static string Episode(EpisodeRef episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return Invariant($"S{episode.Season}{Separator}E{episode.Number}");
    }

    /// <summary>
    /// The primary button names what it will play (D158): the episode for a show, the film by name
    /// (D272). A show with no episode to name is plain "Play".
    /// </summary>
    public static string PlayLabel(CatalogueTitle title, EpisodeRef? episode)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (title.Kind == TitleKind.Film)
        {
            return "Play " + title.Name.Trim();
        }

        return episode is null ? "Play" : "Play " + Episode(episode);
    }

    /// <summary>The picture a home offers, as the where line names it.</summary>
    public static string Quality(VideoQuality quality) => quality switch
    {
        VideoQuality.Uhd => "4K",
        VideoQuality.Hd => "HD",
        VideoQuality.Sd => "SD",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "A picture with no name."),
    };

    /// <summary>The best home (D28): "On Northstar+ · 4K", and ", with ads" when it has them (D29).</summary>
    public static string WhereLine(Home best, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(best);
        var line = "On " + RequireName(serviceName) + Separator + Quality(best.Quality);
        return best.WithAds ? line + ", with ads" : line;
    }

    /// <summary>Another home, listed beneath the best one (D216): "Also on Lanternbox, with ads".</summary>
    public static string AlsoLine(Home other, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(other);
        var line = "Also on " + RequireName(serviceName);
        return other.WithAds ? line + ", with ads" : line;
    }

    /// <summary>A season header's count: "6 episodes · 2 watched", or "8 episodes" when none is watched.</summary>
    public static string SeasonSummary(int episodes, int watched)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(episodes);
        ArgumentOutOfRangeException.ThrowIfNegative(watched);
        var line = Count(episodes, "episode");
        return watched == 0 ? line : line + Separator + Invariant($"{watched} watched");
    }

    /// <summary>A season is headed by its number, as drawn — never the source's name for it, which can give the plot away.</summary>
    public static string SeasonHeading(CatalogueSeason season)
    {
        ArgumentNullException.ThrowIfNull(season);
        return Invariant($"Season {season.Number}");
    }

    private static string Count(int count, string noun) => count == 1 ? Invariant($"1 {noun}") : Invariant($"{count} {noun}s");

    private static string RequireName(string serviceName) =>
        string.IsNullOrWhiteSpace(serviceName)
            ? throw new ArgumentException("A home is named by its service.", nameof(serviceName))
            : serviceName.Trim();

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
