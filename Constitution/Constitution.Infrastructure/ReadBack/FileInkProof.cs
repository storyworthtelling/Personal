using System.Globalization;
using System.Text.Json;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// What the read-back has seen of pen ink, in a file: the sightings so far, and whether they have made the proof
/// (<see cref="InkProof"/>) and on which morning. It is the one thing that turns "no ink anywhere" from a missing round trip
/// (R3) into the owner's silence. A file that cannot be read says "not seen", which is the safe direction: one more morning of
/// caution, not a skip recorded on faith. A file written when one sighting was enough (<c>{"penMarksSeen":true}</c>) is read as
/// established, on no recorded morning.
/// </summary>
public sealed class FileInkProof : IInkProof
{
    private const string DayFormat = "yyyy-MM-dd";

    private static readonly object FileLock = new();

    private readonly string path;

    public FileInkProof(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public InkProof Current
    {
        get
        {
            lock (FileLock)
            {
                return Read();
            }
        }
    }

    public InkProof Record(InkSighting sighting)
    {
        ArgumentNullException.ThrowIfNull(sighting);

        lock (FileLock)
        {
            var before = Read();
            var after = before.Record(sighting);
            if (!ReferenceEquals(before, after))
            {
                AtomicFile.WriteAllText(path, Write(after));
            }

            return after;
        }
    }

    private InkProof Read()
    {
        if (!File.Exists(path))
        {
            return InkProof.None;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return InkProof.None;
            }

            if (root.TryGetProperty("penMarksSeen", out var seen) && seen.ValueKind == JsonValueKind.True)
            {
                if (!root.TryGetProperty("establishedOn", out var on))
                {
                    return InkProof.EstablishedEarlier;
                }

                // A date that cannot be read leaves the proof unsure, and unsure is not proven.
                return on.ValueKind == JsonValueKind.String && Day(on.GetString()) is { } morning
                    ? InkProof.EstablishedOnMorning(morning)
                    : InkProof.None;
            }

            var sightings = new List<InkSighting>();
            if (root.TryGetProperty("sightings", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("document", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString())
                        && item.TryGetProperty("morning", out var day) && day.ValueKind == JsonValueKind.String && Day(day.GetString()) is { } morning)
                    {
                        sightings.Add(new InkSighting(name.GetString()!, morning));
                    }
                }
            }

            return InkProof.Restore(sightings, false, null);
        }
        catch (JsonException)
        {
            // A damaged flag proves nothing; the next record writes a good one.
            return InkProof.None;
        }
    }

    private static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(text, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static string Write(InkProof proof)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("penMarksSeen", proof.IsEstablished);
            if (proof.EstablishedOn is { } on)
            {
                writer.WriteString("establishedOn", on.ToString(DayFormat, CultureInfo.InvariantCulture));
            }

            writer.WriteStartArray("sightings");
            foreach (var sighting in proof.Sightings)
            {
                writer.WriteStartObject();
                writer.WriteString("document", sighting.DocumentName);
                writer.WriteString("morning", sighting.Morning.ToString(DayFormat, CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
