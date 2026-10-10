using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.People;

namespace Pulp.Domain.Watching;

/// <summary>
/// The words on one Continue watching row (mockup 01, D150): what's next ("S1 · E3 · Low Water",
/// "Film · 1h 12m left"), where it plays and how ("On Northstar+ · 4K", "free, with ads" — D28, D29,
/// D121, the service named in text), and the pencil mark's count ("E3 of 6", "46 of 118 min"). The
/// page draws them; nothing here touches a view.
/// </summary>
public static class ContinueRowText
{
    private const string Separator = " · ";

    /// <summary>The count under the pencil mark.</summary>
    public static string Count(ProgressMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        return mark.Unit switch
        {
            ProgressUnit.Episode => $"E{mark.At} of {mark.Of}",
            ProgressUnit.Minute => $"{mark.At} of {mark.Of} min",
            _ => throw new ArgumentOutOfRangeException(nameof(mark), mark.Unit, "A progress unit with no count written for it."),
        };
    }

    /// <summary>How much of the mark's line is drawn: the count's share, held between nothing and all of it.</summary>
    public static double Fraction(ProgressMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        return mark.Of <= 0 ? 0d : Math.Clamp((double)mark.At / mark.Of, 0d, 1d);
    }

    /// <summary>
    /// The row's second line. A show names its next episode, by the catalogue's name when the cache
    /// has one; a film says how long is left. Null for a show whose schedule Pulp does not know (D31),
    /// since there is no next episode to name.
    /// </summary>
    public static string? NextUp(ContinueRow row, CatalogueTitle? title)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Kind == TitleKind.Film)
        {
            return row.Progress is { Unit: ProgressUnit.Minute } mark
                ? "Film" + Separator + Left(Math.Max(0, mark.Of - mark.At))
                : "Film";
        }

        if (row.NextUp is not { } next)
        {
            return null;
        }

        var name = title?.Seasons
            .Where(season => season.Number == next.Season)
            .SelectMany(season => season.Episodes)
            .FirstOrDefault(episode => episode.Episode == next)?.Name;
        return string.IsNullOrWhiteSpace(name) ? Episode(next) : Episode(next) + Separator + name.Trim();
    }

    /// <summary>"1h 12m left", "45m left", "2h left".</summary>
    public static string Left(int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        if (minutes == 0)
        {
            return "Under a minute left";
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        return (hours, rest) switch
        {
            (0, _) => $"{rest}m left",
            (_, 0) => $"{hours}h left",
            _ => $"{hours}h {rest}m left",
        };
    }

    /// <summary>"S1 · E3" — the episode as the row and the card's Play name it.</summary>
    public static string Episode(EpisodeRef episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return $"S{episode.Season}{Separator}E{episode.Number}";
    }

    /// <summary>"E3" — the episode as the card's mark names it.</summary>
    public static string EpisodeShort(EpisodeRef episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return $"E{episode.Number}";
    }

    /// <summary>
    /// Where the title plays and how: the best home on the household's services (D28, with a chosen
    /// home winning, D216), named in text (D121), with its picture — or "free, with ads" for a free
    /// service, which is what that home is (D29). Null when nothing is cached or the title is on none
    /// of the household's services; the row then leaves the line out rather than guess.
    ///
    /// <para>Every subscription is read as ad-free: the household records no plan tier yet (D29's
    /// Settings record is not built), so a "with ads" plan cannot be told from an ad-free one.</para>
    /// </summary>
    public static string? Where(CatalogueTitle? title, IEnumerable<ServiceId> household, HomeOverrides overrides, Func<ServiceId, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(nameOf);
        if (title is null)
        {
            return null;
        }

        if (Home(title, household, overrides) is not { } best)
        {
            return null;
        }

        var how = best.WithAds ? "free, with ads" : Quality(best.Quality);
        return $"On {nameOf(best.Service)}{Separator}{how}";
    }

    /// <summary>
    /// The home the where-line names and Play opens on (D28, D216): <see cref="BestHome"/> over the
    /// household's services, every plan read as ad-free (see <see cref="Where"/>). Null when nothing is
    /// cached or no household service carries the title.
    /// </summary>
    public static Home? Home(CatalogueTitle? title, IEnumerable<ServiceId> household, HomeOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(overrides);
        if (title is null)
        {
            return null;
        }

        var plans = household.Distinct().Select(service => new ServicePlan(service, AdFree: true)).ToList();
        return BestHome.Choose(title.Id, TitleOffers.Streams(title.Offers), plans, overrides).Best;
    }

    /// <summary>The action card's second line (mockup 10): what's next, then the service.</summary>
    public static string? CardSubtitle(string? nextUp, string? serviceName)
    {
        var parts = new[] { nextUp, serviceName }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()).ToList();
        return parts.Count == 0 ? null : string.Join(Separator, parts);
    }

    /// <summary>The picture, as the page names it.</summary>
    public static string Quality(VideoQuality quality) => quality switch
    {
        VideoQuality.Uhd => "4K",
        VideoQuality.Hd => "HD",
        VideoQuality.Sd => "SD",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "A picture quality with no name written for it."),
    };
}

/// <summary>
/// Whose Continue watching this phone shows (D97: each viewer's is private). The household does not
/// yet record which viewer a phone belongs to — Release D's first run does — so the one answer that
/// cannot be wrong is taken: the roster's only adult. With no roster, two adults, or only children,
/// there is nobody, and the page shows its empty state rather than another person's list.
/// </summary>
public static class ContinueWatchingViewer
{
    public static ViewerId? Resolve(HouseholdRoster? roster)
    {
        if (roster is null)
        {
            return null;
        }

        var adults = roster.Viewers.Where(viewer => !viewer.IsChild).ToList();
        return adults.Count == 1 ? adults[0].Id : null;
    }
}
