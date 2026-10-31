using Android.App;
using Codex.Ui.Voice;
using Constitution.Domain.Meals;
using Constitution.Infrastructure.Meals;
using Constitution.MealsTab;
using Microsoft.Extensions.Options;
using Storyworthtelling.Handwriting;
using Xamarin.Android.Net;

namespace Constitution.Services;

/// <summary>
/// Builds the meal estimator on the device (C-6.6.2). Only wiring: the decisions are in Domain and Infrastructure and tested there.
/// The Claude key is read from this install's connections as they are now, so one restored from the vault since the last estimate
/// takes effect on the next; it goes to the estimator's one request header and nowhere else, and is never logged. The photograph is
/// read through <see cref="AtePhotoScaler"/> (shrunk, re-encoded), never straight off the disk.
/// </summary>
internal static class AteEstimatorServices
{
    // A photo through a vision model is a few seconds; this leaves room for a poor connection.
    private static readonly TimeSpan EstimateTimeout = TimeSpan.FromMinutes(2);
    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient(new AndroidMessageHandler()) { Timeout = EstimateTimeout });

    public static IAteEstimator EstimatorOf(Activity host)
    {
        var appContext = host.ApplicationContext ?? host;
        var key = new ConstitutionConnections(appContext).ClaudeApiKey;
        return new ClaudeAteEstimator(Http.Value, Options.Create(new ClaudeVisionOptions { ApiKey = key }), AtePhotoScaler.Read, new CredentialHealthStore());
    }
}
