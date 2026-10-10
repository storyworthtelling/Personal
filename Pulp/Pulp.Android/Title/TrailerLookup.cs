using System.Globalization;
using Android.Content;
using Pulp.App;
using Pulp.Domain.Catalogue;
using Pulp.Infrastructure.Catalogue;
using Pulp.Trailer;

namespace Pulp.Title;

/// <summary>
/// The trailer the quiet "Trailer" button opens (D34, D158), found through the YouTube client and kept
/// on this phone: a found id is kept for good, and "none found" for the day, because every search
/// spends the key's daily quota. No trailer, no key, or a lookup that failed all hide the button —
/// the failure goes to Diagnostics, never onto the page. Nothing here plays: the intent only opens the
/// trailer screen, where the viewer presses play (D77).
/// </summary>
internal sealed class TrailerLookup
{
    /// <summary>The preferences file, named once (never derived from a member name).</summary>
    public const string PreferencesName = "pulp-title-trailers";

    private const string NonePrefix = "none:";

    private readonly ISharedPreferences preferences;
    private readonly PulpServices services;

    public TrailerLookup(Context context, PulpServices services)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Pulp could not open its trailer preferences.");
    }

    /// <summary>The intent that opens this title's trailer, or null when there is none to open. Call off the UI thread.</summary>
    public async Task<Intent?> FindAsync(Context context, CatalogueTitle title, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(title);
        var key = title.Id.Value;
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var kept = preferences.GetString(key, null);

        if (kept == NonePrefix + today)
        {
            return null;
        }

        var videoId = kept is not null && !kept.StartsWith(NonePrefix, StringComparison.Ordinal) ? kept : null;
        if (videoId is null)
        {
            try
            {
                videoId = await services.Trailers.FindTrailerAsync(title.Name, title.Year, cancellationToken);
            }
            catch (CatalogueLookupException exception)
            {
                services.Diagnostics.Warn("trailer", $"No trailer lookup for {title.Id} ({exception.Failure}); the Trailer button stays hidden.", exception);
                return null;
            }

            preferences.Edit()!.PutString(key, string.IsNullOrWhiteSpace(videoId) ? NonePrefix + today : videoId)!.Apply();
        }

        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        try
        {
            return TrailerActivity.For(context, videoId, title.Name);
        }
        catch (ArgumentException exception)
        {
            // The source answered with something that is not a video id; forget it so it is asked again.
            services.Diagnostics.Warn("trailer", $"The trailer id kept for {title.Id} is not a video id; it is forgotten.", exception);
            preferences.Edit()!.Remove(key)!.Apply();
            return null;
        }
    }
}
