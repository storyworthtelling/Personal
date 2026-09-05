using System.Text.Json;

namespace Codex.Domain.Plate;

/// <summary>
/// The face book, written down. Faces are stored as plain numbers on this phone and nowhere
/// else — they are never uploaded, never sent to a model, and deliberately not in the shared
/// vault: the vault is what the household owns, and a biometric measurement of the owner's
/// friends is not something to sync between devices for convenience.
///
/// <para>Garbled or absent reads as an empty book. Losing it costs a few confirmations, and
/// the owner can teach it again; a head that will not launch costs everything.</para>
/// </summary>
public static class FaceBookCodec
{
    private sealed record StoredPerson(string ContactId, string Name, List<float[]> Faces);

    private sealed record StoredBook(List<StoredPerson> People);

    public static string Write(FaceBook book) =>
        JsonSerializer.Serialize(new StoredBook(
            [.. book.People.Select(person =>
                new StoredPerson(person.ContactId, person.Name, [.. person.Faces]))]));

    public static FaceBook Read(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return FaceBook.Empty;
        }

        StoredBook? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<StoredBook>(stored);
        }
        catch (JsonException)
        {
            return FaceBook.Empty;
        }

        var book = FaceBook.Empty;
        foreach (var person in parsed?.People ?? [])
        {
            if (string.IsNullOrEmpty(person.ContactId))
            {
                continue;
            }
            foreach (var face in person.Faces ?? [])
            {
                book = book.Learn(person.ContactId, person.Name ?? string.Empty, face);
            }
        }
        return book;
    }
}
