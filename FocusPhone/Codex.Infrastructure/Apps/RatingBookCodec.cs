using System.Text.Json;
using Codex.Domain.Apps;

namespace Codex.Infrastructure.Apps;

/// <summary>JSON for the rating book. An absent or garbled file is an empty book: every app is looked up again.</summary>
public static class RatingBookCodec
{
    public static string Encode(RatingBook book) => JsonSerializer.Serialize(book.Entries);

    public static RatingBook Decode(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return RatingBook.Empty;
        }
        try
        {
            var entries = JsonSerializer.Deserialize<Dictionary<string, RatingEntry>>(json);
            return entries is null ? RatingBook.Empty : new RatingBook(entries);
        }
        catch (JsonException)
        {
            return RatingBook.Empty;
        }
    }
}
