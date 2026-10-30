using Android.Content;
using Codex.Ui.Voice;
using Constitution.Domain.Meals.Templates;
using Constitution.Infrastructure.Meals;
using Microsoft.Extensions.Options;
using Storyworthtelling.Handwriting;
using Xamarin.Android.Net;

namespace Constitution.Services;

/// <summary>
/// Builds the template author on the device (D110). Only wiring: the decisions are in Domain and Infrastructure and tested there. The Claude
/// key is read from this install's connections each time the author is asked, so one restored from the vault since the last run takes effect
/// on the next; it goes to the author's one request header and nowhere else, and is never logged. With no key the author says so and nothing is
/// proposed: there is no starter set anywhere in the library, so pages 2 and 3 of the sheet stay off until a key and the household's answers exist.
/// </summary>
internal static class TemplateAuthorServices
{
    // A short text reply; this leaves room for a poor connection.
    private static readonly TimeSpan AuthorTimeout = TimeSpan.FromMinutes(2);
    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient(new AndroidMessageHandler()) { Timeout = AuthorTimeout });

    public static ITemplateAuthor AuthorOf(Context appContext)
    {
        ArgumentNullException.ThrowIfNull(appContext);
        return new ClaudeTemplateAuthor(Http.Value, new LiveKey(new ConstitutionConnections(appContext.ApplicationContext ?? appContext)), new CredentialHealthStore());
    }

    // The shared option, with the key as it is at the moment of the call.
    private sealed class LiveKey(ConstitutionConnections connections) : IOptions<ClaudeVisionOptions>
    {
        public ClaudeVisionOptions Value => new() { ApiKey = connections.ClaudeApiKey };
    }
}
