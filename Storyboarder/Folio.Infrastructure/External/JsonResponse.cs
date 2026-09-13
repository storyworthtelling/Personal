using System.Text.Json;

namespace Folio.Infrastructure.External;

// Helpers for pulling structured JSON out of an LLM reply that may be wrapped in stray prose or
// fenced code blocks.
internal static class JsonResponse
{
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Returns the substring spanning the first '{'..last '}' (a JSON object), or null if absent.
    public static string? ExtractObject(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, CaseInsensitive);
}
