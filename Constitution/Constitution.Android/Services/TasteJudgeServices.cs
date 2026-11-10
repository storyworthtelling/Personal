using Storyworthtelling.Voice;
using Android.Content;
using Codex.Ui.Voice;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes.Screens;
using Constitution.Infrastructure.Meals;
using Microsoft.Extensions.Options;
using Storyworthtelling.Handwriting;
using Xamarin.Android.Net;
using Codex.Ui.Http;

namespace Constitution.Services;

/// <summary>
/// Builds the taste judge on the device (D133). Only wiring: the decisions are in Domain and Infrastructure and tested there. The Claude key is read from this install's
/// connections each time the judge is asked, so one restored from the vault since the last run takes effect on the next; it goes to the judge's one request header and
/// nowhere else, and is never logged. With no key the judge says so and the written words stay pending, asked again once a key is set.
/// </summary>
internal static class TasteJudgeServices
{
    // A short JSON reply; this leaves room for a poor connection.
    private static readonly TimeSpan JudgeTimeout = TimeSpan.FromMinutes(1);
    private static HttpClient Http => HeadHttp.WithTimeout(JudgeTimeout);

    /// <summary>The judge, looking dish names up in the recipe box it is handed.</summary>
    public static ITasteJudge JudgeOf(Context appContext, IRecipeBoxSource box)
    {
        ArgumentNullException.ThrowIfNull(appContext);
        ArgumentNullException.ThrowIfNull(box);
        return new ClaudeTasteJudge(
            Http,
            ClaudeKeyOf(new ConstitutionConnections(appContext.ApplicationContext ?? appContext)),
            new TasteDishNames(box).Of,
            HeadCredentialHealth.Store());
    }

    // The key as it is at the moment of the call, so one entered or restored after launch is used.
    private static LiveOptions<ClaudeVisionOptions> ClaudeKeyOf(ConstitutionConnections connections) =>
        new(() => new ClaudeVisionOptions { ApiKey = connections.ClaudeApiKey });
}
