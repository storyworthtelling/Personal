using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codex.Domain.Plate;

/// <summary>
/// The Darkroom, written down.
///
/// <para>It has to survive being closed, because it holds the <b>only</b> copy of everything
/// in it — that is the cost the owner accepted when they reversed the spine, and a room that
/// emptied on a process death would lose their pictures outright.</para>
///
/// <para>Garbled or absent reads as an empty room rather than throwing. The alternative is a
/// head that cannot launch: an empty room is visibly wrong and recoverable, a crash on start
/// is neither. A room the owner has genuinely emptied is kept distinct from that — it round
/// trips as empty rather than being treated as corrupt.</para>
///
/// <para>Choices are not written. A selection is a gesture in progress, not state; coming
/// back to a room with three things silently ticked from yesterday is how somebody deletes
/// the wrong pictures.</para>
/// </summary>
public static class DarkroomCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    private sealed record StoredItem(
        string Id,
        int Kind,
        long CapturedAtTicks,
        string? Caption,
        string? Title,
        int Pages,
        int Destination,
        int Attempts);

    private sealed record StoredRoom(List<StoredItem> Items);

    public static string Write(Darkroom room) =>
        JsonSerializer.Serialize(
            new StoredRoom([.. room.Items.Select(item => new StoredItem(
                item.Id,
                (int)item.Kind,
                item.CapturedAt.Ticks,
                item.Caption,
                item.Title,
                item.Pages,
                (int)item.Destination,
                item.Attempts))]),
            Options);

    public static Darkroom Read(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return Darkroom.Empty;
        }

        StoredRoom? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<StoredRoom>(stored, Options);
        }
        catch (JsonException)
        {
            return Darkroom.Empty;
        }

        if (parsed?.Items is null)
        {
            return Darkroom.Empty;
        }

        var room = Darkroom.Empty;
        foreach (var item in parsed.Items)
        {
            if (string.IsNullOrEmpty(item.Id))
            {
                continue;
            }

            // DateTimeKind.Local because that is what the clock on the row is read in, and
            // ticks alone do not carry it.
            var capturedAt = new DateTime(item.CapturedAtTicks, DateTimeKind.Local);
            var restored = (DarkroomKind)item.Kind == DarkroomKind.Scan
                ? DarkroomItem.Scan(item.Id, capturedAt, item.Title ?? string.Empty, item.Pages,
                    (ScanDestination)item.Destination) with { Attempts = item.Attempts }
                : DarkroomItem.Photo(item.Id, capturedAt);

            room = room.Add(restored with { Caption = item.Caption ?? string.Empty });
        }
        return room;
    }
}
