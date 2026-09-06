using System.Globalization;
using Microsoft.Extensions.Options;
using Foreword.Domain.Health;
using Foreword.Domain.Interfaces;
using Storyworthtelling.Voice.Health;

namespace Foreword.Infrastructure.Nyt;

public class NytCrosswordClient : INytCrosswordClient
{
    private const string BaseUrl = "https://www.nytimes.com/svc/crosswords/v2/puzzle";

    private readonly HttpClient httpClient;
    private readonly NytOptions options;
    private readonly ICredentialHealthSink? healthSink;

    public NytCrosswordClient(
        HttpClient httpClient, IOptions<NytOptions> options, ICredentialHealthSink? healthSink = null)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.healthSink = healthSink;
    }

    public Task<byte[]> DownloadDailyPuzzle(DateOnly date) =>
        DownloadPdf($"{BaseUrl}/print/{PuzzleDateSegment(date)}.pdf");

    public Task<byte[]> DownloadBonusPuzzle(DateOnly date) =>
        DownloadPdf($"{BaseUrl}/print/{PuzzleDateSegment(date)}.pdf");

    // NYT uses MMMddyy format: Apr3026 = April 30, Mar0725 = March 7, 2025
    private static string PuzzleDateSegment(DateOnly date) =>
        date.ToString("MMMddyy", CultureInfo.InvariantCulture);

    private async Task<byte[]> DownloadPdf(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"NYT-S={options.SessionCookie}");

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            // The crossword download is cookie-authenticated, so a 401/403 is the NYT-S cookie being rejected.
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, null))
                healthSink?.MarkFailing(CredentialIds.NytCookie, $"{(int)response.StatusCode} {response.StatusCode}");
            throw new HttpRequestException(
                $"NYT crossword download failed with status {(int)response.StatusCode} ({response.StatusCode}) for {url}",
                inner: null,
                response.StatusCode);
        }

        healthSink?.MarkOk(CredentialIds.NytCookie);
        return await response.Content.ReadAsByteArrayAsync();
    }
}
