using Android.Content;
using Codex.Ui.Voice;
using Pulp.App;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Services;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Discover;

/// <summary>
/// Discover's reach into the availability source: a title search (D209) and the changes feed the week
/// is dealt from. <see cref="PulpServices"/> keeps its Streaming Availability client private and offers
/// no search, so Discover builds its own from the same public pieces (the vault-backed keys, the public
/// service catalogue, the Direct route of D285). Two consequences, reported rather than hidden: these
/// lookups are not on the Diagnostics lookup count (a second counter on the same file would overwrite
/// the first's tallies), and their key rejections still reach credential health through the shared
/// store. Folding this into <see cref="PulpServices"/> is the clean end state.
/// </summary>
internal sealed class DiscoverCatalogue
{
    /// <summary>The changes feed is paged; two pages of new arrivals is a week's worth to deal from.</summary>
    private const int ChangePages = 2;

    private static readonly object InstanceGate = new();
    private static DiscoverCatalogue? instance;

    private readonly StreamingAvailabilityClient availability;

    private DiscoverCatalogue(PulpServices services)
    {
        var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler()) { Timeout = TimeSpan.FromSeconds(30) };
        availability = new StreamingAvailabilityClient(
            http, services.Keys, services.Catalogue, counter: null, new CredentialHealthStore(), StreamingAvailabilityRoute.Direct);
        Http = http;
    }

    /// <summary>The client the poster downloads share.</summary>
    public HttpClient Http { get; }

    public static DiscoverCatalogue For(Context context)
    {
        lock (InstanceGate)
        {
            return instance ??= new DiscoverCatalogue(PulpServices.For(context));
        }
    }

    public Task<IReadOnlyList<CatalogueTitle>> SearchAsync(string title, TitleKind? kind, CancellationToken cancellationToken) =>
        availability.SearchByTitleAsync(title, kind, cancellationToken);

    /// <summary>
    /// What is new on the household's services, whole titles only: the pool the week is dealt from.
    /// A title that turns up on several pages, or on several services, is one candidate.
    /// </summary>
    public async Task<IReadOnlyList<CatalogueTitle>> NewOnServicesAsync(
        IReadOnlyList<StreamingService> services, CancellationToken cancellationToken)
    {
        var titles = new Dictionary<TitleId, CatalogueTitle>();
        string? cursor = null;
        for (var page = 0; page < ChangePages; page++)
        {
            var changes = await availability.GetChangesAsync(services, ChangeKind.New, ChangeItem.Title, cursor, cancellationToken);
            foreach (var (id, title) in changes.Titles)
            {
                titles.TryAdd(id, title);
            }

            if (!changes.HasMore)
            {
                break;
            }

            cursor = changes.NextCursor;
        }

        return titles.Values.ToList();
    }
}
