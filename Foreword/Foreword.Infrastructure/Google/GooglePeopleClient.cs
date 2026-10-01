using System.Net.Http.Headers;
using System.Text.Json;

namespace Foreword.Infrastructure.Google;

/// <summary>
/// The signed-in account's home address — the one Google Maps labels Home — from the People API.
///
/// <para>It takes its token as an argument rather than from <see cref="IGoogleAccessTokenProvider"/>
/// because it needs a scope the morning's delivery does not (<see cref="Scope"/>). Folding it into the
/// delivery token would put a new consent screen in front of the calendar and the tasks, and until
/// somebody answered it the morning would stop; asked for on its own, from Settings, the worst it can
/// do is leave the address field showing.</para>
/// </summary>
public class GooglePeopleClient(HttpClient httpClient)
{
    /// <summary>Read-only access to the account's own addresses, and nothing else.</summary>
    public const string Scope = "https://www.googleapis.com/auth/user.addresses.read";

    private const string Endpoint = "https://people.googleapis.com/v1/people/me?personFields=addresses";

    /// <summary>
    /// The account's home address on one line, or null when there is none — or when Google says no,
    /// which on a settings screen means the same thing: ask for it.
    /// </summary>
    public async Task<string?> HomeAddress(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("addresses", out var addresses)
            || addresses.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        // Home only — a work address is where one person's day goes, not where the household's
        // starts. The primary home first, if Google marks one.
        var homes = addresses.EnumerateArray()
            .Where(address => address.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), "home", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(IsPrimary)
            .ToList();

        foreach (var home in homes)
        {
            if (home.TryGetProperty("formattedValue", out var formatted)
                && OneLine(formatted.GetString()) is { Length: > 0 } line)
            {
                return line;
            }
        }

        return null;
    }

    private static bool IsPrimary(JsonElement address) =>
        address.TryGetProperty("metadata", out var metadata)
        && metadata.TryGetProperty("primary", out var primary)
        && primary.ValueKind == JsonValueKind.True;

    // Google formats an address over lines, the way it would be written on an envelope; the Routes API
    // and a settings row both want it on one.
    private static string OneLine(string? formatted) =>
        string.Join(", ", (formatted ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
