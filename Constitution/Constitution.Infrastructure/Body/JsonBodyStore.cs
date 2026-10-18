using System.Globalization;
using System.Text.Json;
using Constitution.Domain.Body;

namespace Constitution.Infrastructure.Body;

/// <summary>
/// One person's Body readings in a single file in the app's private storage, read and written whole.
///
/// <para><b>Why whole-file, every call.</b> The worker and the screen each build their own store on the same
/// file, and a cached copy in either would lose the other's writes. A file of a few thousand short rows reads
/// in a moment, so every call reads it fresh under one process-wide lock and every write replaces it atomically
/// (<see cref="AtomicFile"/>). Body never leaves the install except through its encrypted backup (D98).</para>
///
/// <para><b>An unreadable file is set aside, not overwritten.</b> The next write would otherwise replace
/// whatever the damage left with a nearly empty file, and the only copy of months of readings would be gone.
/// It is renamed to <c>.unreadable</c> and the store carries on empty, so a backup restore (a merge) refills it. A signal or a row from a newer version is skipped, not fatal.</para>
///
/// <para>The file holds signals, dates and numbers. No name, no label, no source.</para>
/// </summary>
public sealed class JsonBodyStore : IBodyStore
{
    private const int Version = 1;
    private const string DateFormat = "yyyy-MM-dd";

    // One lock for every store in the process: two instances on one file must not interleave read-modify-write.
    private static readonly object FileLock = new();

    private readonly string path;

    public JsonBodyStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    /// <inheritdoc />
    public bool IsEmpty
    {
        get
        {
            lock (FileLock)
            {
                return Load().Readings.Count == 0;
            }
        }
    }

    /// <inheritdoc />
    public DateOnly? InstallDay
    {
        get
        {
            lock (FileLock)
            {
                return Load().InstallDay;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<BodyReading> All()
    {
        lock (FileLock)
        {
            return Ordered(Load().Readings);
        }
    }

    /// <inheritdoc />
    public void Upsert(IEnumerable<BodyReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        lock (FileLock)
        {
            var content = Load();
            foreach (var reading in readings)
            {
                content.Readings[(reading.Signal, reading.Day)] = reading.Value;
            }

            Save(content);
        }
    }

    /// <inheritdoc />
    public void SetInstallDay(DateOnly day)
    {
        lock (FileLock)
        {
            var content = Load();
            if (content.InstallDay is not null)
            {
                return;
            }

            content.InstallDay = day;
            Save(content);
        }
    }

    /// <inheritdoc />
    public bool MergeRestored(DateOnly? installDay, IEnumerable<BodyReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        lock (FileLock)
        {
            var content = Load();
            var changed = false;
            foreach (var reading in readings)
            {
                // A day this install has read stays as this install read it.
                changed |= content.Readings.TryAdd((reading.Signal, reading.Day), reading.Value);
            }

            if (installDay is { } day && (content.InstallDay is null || day < content.InstallDay))
            {
                content.InstallDay = day;
                changed = true;
            }

            if (changed)
            {
                Save(content);
            }

            return changed;
        }
    }

    private static List<BodyReading> Ordered(Dictionary<(BodySignal, DateOnly), double> readings) =>
        readings
            .OrderBy(pair => pair.Key.Item2)
            .ThenBy(pair => BodySignals.All.ToList().IndexOf(pair.Key.Item1))
            .Select(pair => new BodyReading(pair.Key.Item1, pair.Key.Item2, pair.Value))
            .ToList();

    private sealed class Content
    {
        public DateOnly? InstallDay { get; set; }

        public Dictionary<(BodySignal, DateOnly), double> Readings { get; } = [];
    }

    private Content Load()
    {
        var content = new Content();
        if (!File.Exists(path))
        {
            return content;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException e)
        {
            throw new IOException($"The Body readings file could not be read ({e.GetType().Name}).", e);
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            ReadInto(document.RootElement, content);
            return content;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            // Keep what could not be read for a person to look at, and carry on clean (see the class remarks).
            File.Move(path, path + ".unreadable", overwrite: true);
            return new Content();
        }
    }

    private static void ReadInto(JsonElement root, Content content)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The Body readings file is not an object.");
        }

        if (root.TryGetProperty("installDay", out var install)
            && install.ValueKind == JsonValueKind.String
            && TryDate(install.GetString(), out var installDay))
        {
            content.InstallDay = installDay;
        }

        if (!root.TryGetProperty("readings", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object
                && row.TryGetProperty("signal", out var id) && id.ValueKind == JsonValueKind.String
                && BodySignals.TryFromId(id.GetString(), out var signal)
                && row.TryGetProperty("day", out var day) && day.ValueKind == JsonValueKind.String
                && TryDate(day.GetString(), out var date)
                && row.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out var number) && double.IsFinite(number))
            {
                content.Readings[(signal, date)] = number;
            }
        }
    }

    private static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private void Save(Content content)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            if (content.InstallDay is { } install)
            {
                writer.WriteString("installDay", install.ToString(DateFormat, CultureInfo.InvariantCulture));
            }

            writer.WriteStartArray("readings");
            foreach (var reading in Ordered(content.Readings))
            {
                writer.WriteStartObject();
                writer.WriteString("signal", BodySignals.Id(reading.Signal));
                writer.WriteString("day", reading.Day.ToString(DateFormat, CultureInfo.InvariantCulture));
                writer.WriteNumber("value", reading.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        AtomicFile.WriteAllText(path, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
