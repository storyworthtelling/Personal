using Android.App;
using Codex.Ui.Http;
using Codex.Ui.Voice;
using Constitution.Domain.Recipes.Screens;
using Constitution.Infrastructure.Recipes;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice;

namespace Constitution.Services;

/// <summary>
/// Builds the recipe read on the device (D176). Only wiring: the decisions are in Domain and Infrastructure and tested there. The
/// Claude key is read from this install's connections as they are at each read, so one restored from the vault since takes effect
/// on the next; it goes to the reader's one request header and nowhere else, and is never logged.
/// </summary>
internal static class RecipeReaderServices
{
    // Several pages through a vision model can take a while on a poor connection.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMinutes(3);

    public static IRecipePhotoReader ReaderOf(Activity host)
    {
        var appContext = host.ApplicationContext ?? host;
        var connections = new ConstitutionConnections(appContext);
        return new ClaudeRecipeReader(
            HeadHttp.WithTimeout(ReadTimeout),
            new LiveOptions<ClaudeVisionOptions>(() => new ClaudeVisionOptions { ApiKey = connections.ClaudeApiKey }),
            HeadCredentialHealth.Store());
    }
}
