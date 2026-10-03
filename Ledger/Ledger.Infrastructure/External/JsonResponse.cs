using System.Text.Json;

namespace Index.Infrastructure.External;

// Shared JSON options for parsing Plaid replies.
internal static class JsonResponse
{
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
