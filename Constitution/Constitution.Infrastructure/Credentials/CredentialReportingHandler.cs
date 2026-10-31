using Storyworthtelling.Voice.Health;

namespace Constitution.Infrastructure.Credentials;

/// <summary>
/// The reporting seam at an HTTP choke point. After each response it tells the sink whether the
/// credential was rejected (401/403, or Gemini's HTTP 400 API_KEY_INVALID) or worked (success).
/// Everything else says nothing about the credential and is left unreported: throttling (429),
/// server errors (5xx) and other client errors. Exceptions (timeouts, DNS, dropped connections)
/// propagate unchanged and are never reported. A null sink makes the handler a pass-through.
/// </summary>
public sealed class CredentialReportingHandler : DelegatingHandler
{
    private readonly string credentialId;
    private readonly ICredentialHealthSink? sink;

    public CredentialReportingHandler(string credentialId, ICredentialHealthSink? sink)
    {
        if (string.IsNullOrWhiteSpace(credentialId))
            throw new ArgumentException("A credential id is required to report health.", nameof(credentialId));
        this.credentialId = credentialId;
        this.sink = sink;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (sink is null)
            return response;

        var status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            sink.MarkOk(credentialId);
            return response;
        }

        // Only a 400 needs its body read (Gemini's invalid-key shape). Buffer it so the caller can
        // still read the content afterwards.
        string? body = null;
        if (status == 400 && response.Content is not null)
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        if (AuthFailure.IsHttpCredentialRejection(status, body))
            sink.MarkFailing(credentialId, $"{status} {response.StatusCode}");

        return response;
    }
}
