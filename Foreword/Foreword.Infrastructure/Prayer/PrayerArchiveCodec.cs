using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foreword.Domain.Prayer;

namespace Foreword.Infrastructure.Prayer;

/// <summary>
/// Reads and writes the private archive document.
///
/// <para><b>The wire shape is not free to change.</b> The same lesson as the calendar mapping and
/// the stored task — a renamed field deserialises to an empty default rather than failing, and the
/// loss is silent — except that here the loss is not a setting. The journal page is trashed off the
/// tablet once it has been read, so this file is the <b>only</b> copy of what the owner wrote.</para>
///
/// <para>Nothing usable reads as an empty archive rather than throwing: a first morning and a
/// corrupted morning both produce a page with nothing held, because a stack trace inside a delivery
/// costs the whole morning and an empty list costs one page.</para>
///
/// <para><see cref="Write"/> takes an <see cref="IPrayerReader"/> because writing is the one moment
/// the text must become a string. It borrows the caller's permission rather than declaring its own,
/// so the list of things allowed to read prayer content stays at four.</para>
/// </summary>
public static class PrayerArchiveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public static PrayerArchive Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PrayerArchive();
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredArchive>(json, Options);
            if (stored is null)
            {
                return new PrayerArchive();
            }

            return new PrayerArchive
            {
                Requests = [.. stored.Requests.Select(ToRequest)],
                Journal = [.. stored.Journal.Select(ToEntry)],
                Rotations = [.. stored.Rotations.Select(ToRotation)],
            };
        }
        catch (JsonException)
        {
            return new PrayerArchive();
        }
        catch (FormatException)
        {
            // A field that is present but not a date. Same answer as a garbled document: the page
            // is worth more empty than the delivery is worth stopped.
            return new PrayerArchive();
        }
    }

    public static string Write(PrayerArchive archive, IPrayerReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var stored = new StoredArchive
        {
            Requests = [.. archive.Requests.Select(request => new StoredRequest
            {
                Id = request.Id,
                Text = request.Text.ReadBy(reader),
                Held = Stamp(request.Held),
                Answered = request.Answered is { } answered ? Stamp(answered) : null,
                StrikePrinted = request.StrikePrinted,
            })],
            Journal = [.. archive.Journal.Select(entry => new StoredEntry
            {
                Date = Stamp(entry.Date),
                Text = entry.Text.ReadBy(reader),
            })],
            Rotations = [.. archive.Rotations.Select(rotation => new StoredRotation
            {
                Id = rotation.Id,
                Name = rotation.Name,
                Members = [.. rotation.Members.Select(member => member.ReadBy(reader))],
                Began = Stamp(rotation.Began),
                Paused = rotation.Paused,
            })],
        };

        return JsonSerializer.Serialize(stored, Options);
    }

    private static PrayerRequest ToRequest(StoredRequest stored) => new()
    {
        Id = stored.Id ?? string.Empty,
        Text = PrayerText.Of(stored.Text),
        Held = Parse(stored.Held),
        Answered = string.IsNullOrWhiteSpace(stored.Answered) ? null : Parse(stored.Answered),
        StrikePrinted = stored.StrikePrinted,
    };

    private static PrayerRotation ToRotation(StoredRotation stored) => new()
    {
        Id = stored.Id ?? string.Empty,
        Name = stored.Name ?? string.Empty,
        Members = [.. (stored.Members ?? []).Select(PrayerText.Of)],
        Began = Parse(stored.Began),
        Paused = stored.Paused,
    };

    private static JournalEntry ToEntry(StoredEntry stored) => new()
    {
        Date = Parse(stored.Date),
        Text = PrayerText.Of(stored.Text),
    };

    /// <summary>
    /// Round-trip safe and sortable, so the tablet's own list and a person reading the raw file see
    /// the same order.
    /// </summary>
    private static string Stamp(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly Parse(string? stamp) =>
        DateOnly.ParseExact(stamp ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The stored shape, separate from the domain records so the field names on Drive are written
    // down in one place rather than being whatever a property happens to be called this month.
    private sealed record StoredArchive
    {
        public IReadOnlyList<StoredRequest> Requests { get; init; } = [];

        public IReadOnlyList<StoredEntry> Journal { get; init; } = [];

        public IReadOnlyList<StoredRotation> Rotations { get; init; } = [];
    }

    private sealed record StoredRotation
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public IReadOnlyList<string>? Members { get; init; }

        public string? Began { get; init; }

        public bool Paused { get; init; }
    }

    private sealed record StoredRequest
    {
        public string? Id { get; init; }

        public string? Text { get; init; }

        public string? Held { get; init; }

        public string? Answered { get; init; }

        public bool StrikePrinted { get; init; }
    }

    private sealed record StoredEntry
    {
        public string? Date { get; init; }

        public string? Text { get; init; }
    }
}
