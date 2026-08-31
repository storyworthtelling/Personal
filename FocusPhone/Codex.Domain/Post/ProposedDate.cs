using System.Text.Json;

namespace Codex.Domain.Post;

/// <summary>
/// A date a message proposed, with what the calendar said about that day — true it
/// clashed, false it was clear, null nobody could look.
/// </summary>
public sealed record ProposedDate(ProposedEvent Event, bool? Clashed);

/// <summary>
/// Keeps a proposal between the sync that found it and the screen that offers it. Detection
/// runs in the background worker, where there is nothing to offer anything on.
///
/// JSON rather than a delimited record, because the title comes from a subject line that
/// somebody else wrote — any separator not escaped is one they can put in a subject.
/// </summary>
public static class ProposedDateCodec
{
    public static string Encode(ProposedDate offer) => JsonSerializer.Serialize(offer);

    /// <summary>
    /// The offer, or null if there is none, it cannot be read, or its day has passed. An
    /// offer to add last week's recital is worse than no offer at all.
    /// </summary>
    public static ProposedDate? Decode(string? stored, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            var offer = JsonSerializer.Deserialize<ProposedDate>(stored);
            if (offer?.Event is null || offer.Event.StartsOn < today)
            {
                return null;
            }
            return offer;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
