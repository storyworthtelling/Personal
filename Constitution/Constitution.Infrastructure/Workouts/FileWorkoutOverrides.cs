using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Calendar;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// The owner's moves and skips as the one small JSON text they are kept as. It holds who, which session (by its calendar key,
/// never a date in it), the day it left, where it went and what the session was: a kind, a length, whether it is sweaty and
/// the exercises by name. No dose, no load, no page and no voiced line.
/// </summary>
public static class PlanOverridesCodec
{
    private const int Version = 1;
    private const string DayFormat = "yyyy-MM-dd";
    private const string DurationFormat = "c";

    public static string Encode(PlanOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("moves");
            foreach (var group in overrides.Groups)
            {
                writer.WriteStartArray();
                foreach (var move in group.Moves)
                {
                    Write(writer, move);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("skips");
            foreach (var skip in overrides.Skips)
            {
                writer.WriteStartObject();
                writer.WriteString("person", skip.Person);
                writer.WriteString("day", skip.Day.ToString(DayFormat, CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <exception cref="FormatException">The text is not a record of the owner's moves this app can read, with the reason.</exception>
    public static PlanOverrides Decode(string text)
    {
        try
        {
            using var parsed = JsonDocument.Parse(text);
            var root = parsed.RootElement;
            if (root.GetProperty("version").GetInt32() != Version)
            {
                throw new FormatException("This record is of a version this app does not read.");
            }

            var groups = root.GetProperty("moves").EnumerateArray()
                .Select(group => new MoveGroup(group.EnumerateArray().Select(ReadMove).ToList()))
                .ToList();
            var skips = root.GetProperty("skips").EnumerateArray()
                .Select(skip => new SessionSkipped(Text(skip, "person"), Day(skip, "day")))
                .ToList();
            return new PlanOverrides(groups, skips);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new FormatException("The record could not be read as the owner's moves on the Workouts tab.", e);
        }
    }

    private static void Write(Utf8JsonWriter writer, SessionMoved move)
    {
        writer.WriteStartObject();
        writer.WriteString("person", move.Person);
        writer.WriteString("key", move.Key);
        writer.WriteString("fromDay", move.FromDay.ToString(DayFormat, CultureInfo.InvariantCulture));
        writer.WriteString("day", move.Day.ToString(DayFormat, CultureInfo.InvariantCulture));
        writer.WriteString("start", move.Start.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("gapStart", move.Gap.Start.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("gapEnd", move.Gap.End.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("kind", move.Session.Kind.ToString());
        writer.WriteString("duration", move.Session.Duration.ToString(DurationFormat, CultureInfo.InvariantCulture));
        writer.WriteBoolean("sweaty", move.Session.IsSweaty);
        writer.WriteStartArray("exercises");
        foreach (var exercise in move.Session.Exercises)
        {
            writer.WriteStartObject();
            writer.WriteString("name", exercise.Name);
            if (exercise.EquipmentId is { } equipment)
            {
                writer.WriteString("equipment", equipment);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static SessionMoved ReadMove(JsonElement item)
    {
        var kind = Enum.Parse<SessionKind>(Text(item, "kind"));
        if (!Enum.IsDefined(kind))
        {
            throw new FormatException("The record names a kind of session this app does not know.");
        }

        var exercises = item.GetProperty("exercises").EnumerateArray()
            .Select(exercise => new Exercise(Text(exercise, "name"), exercise.TryGetProperty("equipment", out var equipment) ? equipment.GetString() : null))
            .ToList();
        var session = new Session(
            kind,
            TimeSpan.ParseExact(Text(item, "duration"), DurationFormat, CultureInfo.InvariantCulture),
            item.GetProperty("sweaty").GetBoolean(),
            exercises);
        return new SessionMoved(
            Text(item, "person"),
            Text(item, "key"),
            Day(item, "fromDay"),
            Day(item, "day"),
            Moment(item, "start"),
            new FreeGap(Moment(item, "gapStart"), Moment(item, "gapEnd")),
            session);
    }

    private static DateOnly Day(JsonElement item, string name) => DateOnly.ParseExact(Text(item, name), DayFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset Moment(JsonElement item, string name) =>
        DateTimeOffset.Parse(Text(item, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text
            : throw new FormatException($"The record has no text called '{name}' where one belongs.");
}

/// <summary>
/// The owner's moves in one file in the app's private storage, read and written whole on every call. A file that cannot be
/// read is set aside (<see cref="StoredFiles.SetAside"/>) and the store carries on empty: that costs the moves in it (the week
/// is planned as the planner made it, the same as before the moves were kept), never the morning's run.
/// </summary>
public sealed class FileWorkoutOverrides : IWorkoutOverrides
{
    private static readonly object FileLock = new();

    private readonly string path;

    public FileWorkoutOverrides(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public PlanOverrides Load()
    {
        lock (FileLock)
        {
            return Read();
        }
    }

    public void Add(PlanOverrides more)
    {
        ArgumentNullException.ThrowIfNull(more);
        lock (FileLock)
        {
            var kept = Read();
            var skips = kept.Skips.ToList();
            skips.AddRange(more.Skips.Where(skip => !skips.Any(held => string.Equals(held.Person, skip.Person, StringComparison.OrdinalIgnoreCase) && held.Day == skip.Day)));
            AtomicFile.WriteAllText(path, PlanOverridesCodec.Encode(new PlanOverrides([.. kept.Groups, .. more.Groups], skips)));
        }
    }

    private PlanOverrides Read()
    {
        if (!File.Exists(path))
        {
            return PlanOverrides.None;
        }

        try
        {
            return PlanOverridesCodec.Decode(File.ReadAllText(path));
        }
        catch (FormatException)
        {
            StoredFiles.SetAside(path);
            return PlanOverrides.None;
        }
    }
}
