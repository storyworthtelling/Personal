using System.Text.Json;

namespace Codex.Domain.Post;

/// <summary>
/// Persists field notes between launches. They accumulate across a whole test-plan run —
/// days of it — so losing them on a process death would leave the owner's phone time
/// producing nothing.
///
/// Anything unreadable decodes to fresh notes: these are observations, and failing to read
/// them back is not worth an error on a screen.
/// </summary>
public static class FieldNotesCodec
{
    public static string Encode(FieldNotes notes) => JsonSerializer.Serialize(notes);

    public static FieldNotes Decode(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return FieldNotes.Fresh;
        }

        try
        {
            return JsonSerializer.Deserialize<FieldNotes>(stored) ?? FieldNotes.Fresh;
        }
        catch (JsonException)
        {
            return FieldNotes.Fresh;
        }
    }
}
