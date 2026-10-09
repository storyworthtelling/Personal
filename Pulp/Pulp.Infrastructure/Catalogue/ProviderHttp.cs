using System.Net;
using System.Text.Json;
using Pulp.Domain.Catalogue;
using Storyworthtelling.Voice.Health;

namespace Pulp.Infrastructure.Catalogue;

/// <summary>A response as read: its status, body and redirect target.</summary>
internal sealed record ProviderResponse(HttpStatusCode Status, string Body, Uri? Location)
{
    public int Code => (int)Status;
}

/// <summary>
/// The one send path the four clients share: count the lookup (D261), send, read the body, and
/// classify a failure. Auth rejections are reported to the health sink under the client's credential
/// id; throttling and server trouble are not, since they are not the key's fault.
/// </summary>
internal sealed class ProviderHttp
{
    private readonly HttpClient httpClient;
    private readonly LookupSource source;
    private readonly string? credentialId;
    private readonly ILookupCounter? counter;
    private readonly ICredentialHealthSink? healthSink;
    private readonly Func<ProviderResponse, bool> isThrottle;

    public ProviderHttp(
        HttpClient httpClient,
        LookupSource source,
        string? credentialId,
        ILookupCounter? counter,
        ICredentialHealthSink? healthSink,
        Func<ProviderResponse, bool>? isThrottle = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        this.source = source;
        this.credentialId = credentialId;
        this.counter = counter;
        this.healthSink = healthSink;
        this.isThrottle = isThrottle ?? (response => false);
    }

    /// <summary>The key, or a <see cref="LookupFailure.MissingKey"/> failure before anything is sent.</summary>
    public string RequireKey(string? key, string what) =>
        string.IsNullOrWhiteSpace(key)
            ? throw new CatalogueLookupException(source, LookupFailure.MissingKey, null,
                $"No {what} key is set, so nothing was asked of {source}; the key is entered in Settings or restored from the vault.")
            : key.Trim();

    /// <summary>
    /// Sends a GET and returns the response when it succeeded, or when its status is one the caller
    /// treats as an answer (<paramref name="answers"/>: a 404 that means "not known", a redirect to follow).
    /// </summary>
    public async Task<ProviderResponse> GetAsync(
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        string what,
        CancellationToken cancellationToken,
        params HttpStatusCode[] answers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        counter?.Counted(source);
        ProviderResponse response;
        try
        {
            using var message = await httpClient.SendAsync(request, cancellationToken);
            var body = await message.Content.ReadAsStringAsync(cancellationToken);
            response = new ProviderResponse(message.StatusCode, body, message.Headers.Location);
        }
        catch (HttpRequestException exception)
        {
            throw new CatalogueLookupException(source, LookupFailure.Unavailable, null,
                $"{source} could not be reached for {what}: {exception.Message}", exception);
        }

        if (response.Code is >= 200 and < 300)
        {
            MarkOk();
            return response;
        }

        if (answers.Contains(response.Status))
        {
            // A documented non-success answer still means the key was accepted.
            MarkOk();
            return response;
        }

        throw Classify(response, what);
    }

    /// <summary>Parses a body the source documents as JSON, or fails as <see cref="LookupFailure.Malformed"/>.</summary>
    public JsonDocument Parse(ProviderResponse response, string what)
    {
        try
        {
            return JsonDocument.Parse(response.Body);
        }
        catch (JsonException exception)
        {
            throw Malformed(what, exception.Message, exception);
        }
    }

    public CatalogueLookupException Malformed(string what, string detail, Exception? inner = null) =>
        new(source, LookupFailure.Malformed, null, $"{source} answered {what} with a body that is not the documented shape: {detail}", inner);

    private CatalogueLookupException Classify(ProviderResponse response, string what)
    {
        var detail = $"{source} answered {what} with HTTP {response.Code}: {Excerpt(response.Body)}";
        if (response.Status == HttpStatusCode.TooManyRequests || isThrottle(response))
        {
            return new CatalogueLookupException(source, LookupFailure.Throttled, response.Code, detail);
        }

        if (credentialId is not null && AuthFailure.IsHttpCredentialRejection(response.Code, response.Body))
        {
            healthSink?.MarkFailing(credentialId, $"HTTP {response.Code}");
            return new CatalogueLookupException(source, LookupFailure.Rejected, response.Code, detail);
        }

        return new CatalogueLookupException(source, LookupFailure.Unavailable, response.Code, detail);
    }

    private void MarkOk()
    {
        if (credentialId is not null)
        {
            healthSink?.MarkOk(credentialId);
        }
    }

    /// <summary>The start of an error body, for the message; a source's error text never echoes the key back.</summary>
    private static string Excerpt(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }
}

/// <summary>Small readers over <see cref="JsonElement"/> that treat a missing or null field as absent.</summary>
internal static class Json
{
    public static string? Text(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? Number(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    public static long? Long(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    public static bool Flag(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static JsonElement Child(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    public static IEnumerable<JsonElement> Items(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];
}
