using Pulp.App;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Friday;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Services;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Playlists;

/// <summary>What making a playlist came to: the saved playlist, or the quiet sentence that says why nothing was saved.</summary>
internal sealed record MadeOutcome(HouseholdPlaylist? Saved, string? Status);

/// <summary>
/// The model-made playlist (D104, built without a mockup by D272): a prompt or a seed playlist goes to
/// the mood client as a <see cref="PlaylistQuery"/>, each name it answers is searched in the catalogue,
/// <see cref="PlaylistPlan.Resolve"/> keeps only real titles on the household's services, and the result
/// is saved as an ordinary hand-orderable <see cref="HouseholdPlaylist"/> (D103) — the viewer's own, or
/// shared when they chose so. "No idea" from the model, or nothing left once resolved, is one quiet
/// status sentence and nothing is saved; neither is ever voiced (D99, G12). Runs off the UI thread.
///
/// <para>Each suggested name is searched through <see cref="PulpServices.SearchAsync"/>, the one
/// counted client, so a made playlist's searches are on the month's lookup count (D261).</para>
/// </summary>
internal sealed class PlaylistMaker(PulpServices services)
{
    public const string NoIdea = "No playlist came of that, so nothing was saved.";
    public const string NothingOnOurServices = "None of the suggestions are on your services, so nothing was saved.";
    public const string Asking = "Asking for titles…";

    public async Task<MadeOutcome> MakeAsync(
        PlaylistSeed seed,
        ViewerId viewer,
        bool shared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);

        var view = await services.HouseholdAsync(cancellationToken);
        var roster = view.Roster ?? throw new InvalidOperationException("A playlist was made with nobody on the roster.");
        var mood = MoodViewer.Adult(viewer);
        var householdServices = view.Services.Select(services.Catalogue.ById).OfType<StreamingService>().ToList();

        // Taste is Discover's to learn (D23); the made playlist sends the services and the words only.
        var context = DiscoverContext.For(mood, roster, householdServices, TasteProfile.Empty);
        var query = PlaylistQuery.For(seed, context);

        var answer = await services.Mood.AskPlaylistAsync(query, cancellationToken);
        if (answer.Outcome != MoodOutcome.Answered)
        {
            services.Diagnostics.Warn("playlists", $"A made playlist came back with no idea ({answer.Outcome}). {answer.Detail}");
            return new MadeOutcome(null, NoIdea);
        }

        var results = new List<CatalogueTitle>();
        var terms = MoodPlan.SearchTerms(answer);
        foreach (var term in terms)
        {
            try
            {
                results.AddRange(await services.SearchAsync(term, cancellationToken));
            }
            catch (CatalogueLookupException exception)
            {
                services.Diagnostics.Warn("playlists", $"A search for a suggested title failed ({exception.Failure}); that suggestion is dropped.", exception);
            }
        }

        var made = PlaylistPlan.Resolve(query, answer, results, mood, _ => Array.Empty<FranchiseId>());
        if (made.Titles.Count == 0)
        {
            return new MadeOutcome(null, NothingOnOurServices);
        }

        var playlist = new HouseholdPlaylist(NewId(), viewer, shared, made.ToPlaylist());
        await services.PutAsync(HouseholdMaps.Playlist, playlist, cancellationToken);
        return new MadeOutcome(playlist, null);
    }

    public static PlaylistId NewId() => new("playlist-" + Guid.NewGuid().ToString("N"));
}
