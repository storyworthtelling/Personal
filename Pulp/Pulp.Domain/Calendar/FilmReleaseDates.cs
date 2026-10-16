using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Services;

namespace Pulp.Domain.Calendar;

/// <summary>
/// The day a film arrives on one of the household's services, as the availability source announces it
/// (D302). The catalogue holds a film's release year only, so this is the single route a film's date takes
/// to the calendar: it is never inferred from the year and never made up.
/// </summary>
public sealed record FilmReleaseDate(TitleId Title, ServiceId Service, DateOnly Date);

/// <summary>Where the calendar learns film dates; the head wires the availability source behind it.</summary>
public interface IFilmReleaseSource
{
    Task<IReadOnlyList<FilmReleaseDate>> UpcomingAsync(CancellationToken cancellationToken);
}

/// <summary>Reads one page of the changes feed; the head passes the counted availability client's method.</summary>
public delegate Task<ChangesPage> ChangesReader(
    IEnumerable<StreamingService> services, ChangeKind kind, ChangeItem item, string? cursor, CancellationToken cancellationToken);

public static class FilmReleaseDates
{
    /// <summary>
    /// Upcoming changes of a whole film, on a service the household pays for (subscription, free or add-on),
    /// with an announced day. A rental or purchase window is not "the film's release" on a service the
    /// household has, and an undated change is "no date", not a guess. Several services for one film: the
    /// earliest day wins.
    /// </summary>
    public static IReadOnlyList<FilmReleaseDate> FromChanges(IEnumerable<CatalogueChange> changes, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(zone);

        return changes
            .Where(change => change is
            {
                Kind: ChangeKind.Upcoming,
                Item: ChangeItem.Title,
                TitleKind: TitleKind.Film,
                Offer: OfferKind.Subscription or OfferKind.Free or OfferKind.Addon,
                At: not null,
            })
            .Select(change => new FilmReleaseDate(
                change.Title,
                change.Service,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(change.At!.Value, zone).DateTime)))
            .GroupBy(date => date.Title)
            .Select(group => group.OrderBy(date => date.Date).First())
            .ToList();
    }
}

/// <summary>
/// The upcoming-title changes of the household's services, a bounded number of pages. The feed is counted
/// against the source's monthly budget, so the page count is a cap and not a loop to the end.
/// </summary>
public sealed class ChangesFilmReleaseSource(
    IReadOnlyList<StreamingService> services, ChangesReader read, TimeZoneInfo zone, int maxPages) : IFilmReleaseSource
{
    public async Task<IReadOnlyList<FilmReleaseDate>> UpcomingAsync(CancellationToken cancellationToken)
    {
        if (services.Count == 0)
        {
            return [];
        }

        var changes = new List<CatalogueChange>();
        string? cursor = null;
        for (var page = 0; page < maxPages; page++)
        {
            var result = await read(services, ChangeKind.Upcoming, ChangeItem.Title, cursor, cancellationToken);
            changes.AddRange(result.Changes);
            if (!result.HasMore)
            {
                break;
            }

            cursor = result.NextCursor;
        }

        return FilmReleaseDates.FromChanges(changes, zone);
    }
}

/// <summary>
/// Remembers the last answer for a lifetime, so opening the Calendar page does not spend a request each time.
/// Callers that arrive while a fetch is under way share it rather than each starting their own, one caller's
/// cancellation does not cancel it for the rest, and a failed fetch is not kept. The answer is kept per
/// <paramref name="keyOf"/> (the ticked services): a changed tick is a different question and asks again.
/// </summary>
public sealed class CachedFilmReleaseSource(
    IFilmReleaseSource inner, Func<DateTimeOffset> now, TimeSpan lifetime, Func<CancellationToken, Task<string>>? keyOf = null) : IFilmReleaseSource
{
    private readonly object gate = new();
    private IReadOnlyList<FilmReleaseDate>? held;
    private DateTimeOffset heldAt;
    private string? heldKey;
    private Task<IReadOnlyList<FilmReleaseDate>>? inflight;
    private string? inflightKey;
    private int inflightId;

    public async Task<IReadOnlyList<FilmReleaseDate>> UpcomingAsync(CancellationToken cancellationToken)
    {
        var key = keyOf is null ? string.Empty : await keyOf(cancellationToken);
        Task<IReadOnlyList<FilmReleaseDate>> shared;
        lock (gate)
        {
            if (held is not null && heldKey == key && now() - heldAt < lifetime)
            {
                return held;
            }

            if (inflight is null || inflightKey != key)
            {
                var id = ++inflightId;
                inflightKey = key;
                inflight = Task.Run(() => FetchAsync(key, id));
            }

            shared = inflight;
        }

        return await shared.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<FilmReleaseDate>> FetchAsync(string key, int id)
    {
        try
        {
            // Its own token: the fetch is shared, so no single caller's cancellation may end it for the others.
            var fresh = await inner.UpcomingAsync(CancellationToken.None);
            lock (gate)
            {
                held = fresh;
                heldKey = key;
                heldAt = now();
            }

            return fresh;
        }
        finally
        {
            lock (gate)
            {
                if (inflightId == id)
                {
                    inflight = null;
                }
            }
        }
    }
}
