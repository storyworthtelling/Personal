using System.Globalization;
using Microsoft.Extensions.Options;
using NytRemarkable.Domain.Interfaces;

namespace NytRemarkable.Infrastructure.Nyt;

public class NytCrosswordClient : INytCrosswordClient
{
    private const string BaseUrl = "https://www.nytimes.com/svc/crosswords/v2/puzzle";

    private readonly HttpClient httpClient;
    private readonly NytOptions options;

    public NytCrosswordClient(HttpClient httpClient, IOptions<NytOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
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
            throw new HttpRequestException(
                $"NYT crossword download failed with status {(int)response.StatusCode} ({response.StatusCode}) for {url}",
                inner: null,
                response.StatusCode);

        return await response.Content.ReadAsByteArrayAsync();
    }
}
