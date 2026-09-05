using System.Text.Json;

namespace Codex.Domain.Post;

/// <summary>A date a message proposes. Title and day are required; a time is optional.</summary>
public sealed record ProposedEvent(string Title, DateOnly StartsOn, TimeOnly? StartsAt);

/// <summary>What one reading of a message concluded. Either half may be absent.</summary>
public sealed record MailReading(MailClass? Kind, ProposedEvent? Event)
{
    public static MailReading Nothing { get; } = new(null, null);

    /// <summary>
    /// Reads a model's reply. Nothing here throws: this runs inside the sync, and a sync
    /// that dies on a malformed reply stops mail arriving at all — which is a far worse
    /// outcome than a message going unclassified.
    ///
    /// A kind nobody offered means the model improvised, and the safe answer is no answer:
    /// an invented kind could age a real message out in a day. An event missing its date or
    /// its title is dropped for the same reason — half a date would raise a dog-ear on a
    /// thread and then have nothing to propose.
    /// </summary>
    public static MailReading Parse(string? reply)
    {
        var json = ExtractJson(reply);
        if (json is null)
        {
            return Nothing;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Nothing;
            }

            return new MailReading(ReadKind(document.RootElement), ReadEvent(document.RootElement));
        }
        catch (JsonException)
        {
            return Nothing;
        }
    }

    // Models wrap JSON in prose or a fence however firmly they are told not to, so the
    // object is found rather than assumed to be the whole reply.
    private static string? ExtractJson(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return null;
        }
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        return start >= 0 && end > start ? reply[start..(end + 1)] : null;
    }

    private static MailClass? ReadKind(JsonElement root)
    {
        if (!root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return Enum.TryParse<MailClass>(kind.GetString(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }

    private static ProposedEvent? ReadEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = Text(element, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }
        if (!DateOnly.TryParse(Text(element, "startsOn"), out var startsOn))
        {
            return null;
        }

        var startsAt = TimeOnly.TryParse(Text(element, "startsAt"), out var time) ? time : (TimeOnly?)null;
        return new ProposedEvent(title!, startsOn, startsAt);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
