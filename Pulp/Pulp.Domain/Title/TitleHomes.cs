using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Services;
using Pulp.Domain.Voice;

namespace Pulp.Domain.Title;

/// <summary>
/// Where the title page says a title is (D28, D29, D216) and which link Play asks the hand-off for.
/// The choosing is <see cref="BestHome"/>'s; this reads the catalogue's offers into it and the link
/// back out.
/// </summary>
public static class TitleHomes
{
    /// <summary>
    /// The best home among the household's services, and the others beneath it. <b>Ad tiers are not
    /// known by service id alone</b>: every service is taken as ad-free and only a free, ad-supported offer
    /// says "with ads". Screens that hold the household use the <see cref="ServicePlan"/> overload.
    /// </summary>
    public static HomeChoice Choose(CatalogueTitle title, IEnumerable<ServiceId> household, HomeOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(household);
        return Choose(title, household.Distinct().Select(service => new ServicePlan(service, AdFree: true)).ToList(), overrides);
    }

    /// <summary>
    /// The same choice over the household's recorded plans (<c>HouseholdView.ServicePlans</c>): a with-ads
    /// subscription says so and loses a tie (D29), and the plan's picture caps what is claimed (D28, D86).
    /// </summary>
    public static HomeChoice Choose(CatalogueTitle title, IEnumerable<ServicePlan> plans, HomeOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(overrides);

        return BestHome.Choose(title.Id, TitleOffers.Streams(title.Offers), plans, overrides);
    }

    /// <summary>
    /// The link to hand off for <paramref name="service"/>: the episode's own offer there when it has
    /// one, else the title's, best picture first. Only https links are kept — a hand-off sends nothing
    /// else — and null means the service gave nothing that can be opened.
    /// </summary>
    public static HandOffLink? LinkFor(CatalogueTitle title, ServiceId service, EpisodeRef? episode)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (episode is not null
            && TitleProgress.Episodes(title).FirstOrDefault(candidate => candidate.Episode == episode) is { } listed
            && Link(title.Id, service, listed.Offers) is { } episodeLink)
        {
            return episodeLink;
        }

        return Link(title.Id, service, title.Offers);
    }

    /// <summary>What the playing app's session might call it (D122): the title, and the episode's name.</summary>
    public static IReadOnlyList<string> SessionNames(CatalogueTitle title, EpisodeRef? episode)
    {
        ArgumentNullException.ThrowIfNull(title);
        var names = new List<string> { title.Name };
        if (episode is not null
            && TitleProgress.Episodes(title).FirstOrDefault(candidate => candidate.Episode == episode) is { } listed
            && !string.IsNullOrWhiteSpace(listed.Name))
        {
            names.Add(listed.Name);
        }

        return names;
    }

    private static HandOffLink? Link(TitleId title, ServiceId service, IEnumerable<TitleOffer> offers) =>
        offers
            .Where(offer => offer.Service.Equals(service) && offer.Kind is OfferKind.Subscription or OfferKind.Free)
            .OrderByDescending(offer => offer.Quality)
            .Select(offer => Https(offer.Link) is { } link ? new HandOffLink(title, service, link, Https(offer.VideoLink)) : null)
            .FirstOrDefault(link => link is not null);

    private static Uri? Https(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
}

/// <summary>
/// The past titles the why-this line may draw on (D36, D41): the ones this viewer finished that share
/// a genre with the title on the page. "Like it" is a shared genre because that is all the catalogue
/// carries; <see cref="VoiceFactBuilder.WhyThis"/> still takes only the latest one, and only for the
/// device owner. A film is finished when the watched rule says so; a show when its last listed episode
/// is. A title the cache cannot name is skipped rather than named by its id.
/// </summary>
public static class WhyThisPast
{
    public static IReadOnlyList<PastTitle> Alike(
        ViewerId viewer, CatalogueTitle title, IEnumerable<WatchEvent> sittings, Func<TitleId, CatalogueTitle?> lookup)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(sittings);
        ArgumentNullException.ThrowIfNull(lookup);

        var genres = title.Genres.Where(genre => !string.IsNullOrWhiteSpace(genre)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (genres.Count == 0)
        {
            return [];
        }

        var past = new List<PastTitle>();
        foreach (var group in sittings.Where(sitting => sitting.Kind != WatchedKind.Game && !sitting.Title.Equals(title.Id)).GroupBy(sitting => sitting.Title))
        {
            var known = lookup(group.Key);
            if (known is null || string.IsNullOrWhiteSpace(known.Name) || !known.Genres.Any(genres.Contains) || !Finished(known, group.ToList()))
            {
                continue;
            }

            past.Add(new PastTitle(viewer, known.Name.Trim(), group.Max(sitting => sitting.At)));
        }

        return past.OrderByDescending(entry => entry.FinishedAt).ToList();
    }

    private static bool Finished(CatalogueTitle known, IReadOnlyList<WatchEvent> sittings)
    {
        if (known.Kind == TitleKind.Film)
        {
            return Sittings.Verdict(sittings).IsWatched;
        }

        var last = TitleProgress.Episodes(known).Select(episode => episode.Episode).Max();
        return last is not null && TitleProgress.WatchedEpisodes(known.Id, sittings).Contains(last);
    }
}
