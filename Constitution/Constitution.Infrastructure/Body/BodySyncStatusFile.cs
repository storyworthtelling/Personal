using System.Globalization;
using System.Text.Json;
using Constitution.Domain.Body;
using Constitution.Domain.Diagnostics;

namespace Constitution.Infrastructure.Body;

/// <summary>What the last Body sync was, as Diagnostics reports it.</summary>
public sealed record BodySyncRecord(BodySyncTrigger Trigger, BodySyncGap Gap, DiagnosticsOutcome Outcome, DateTimeOffset At);

/// <summary>
/// Remembers the last Body sync that ran, for the Diagnostics export (C-7.1.3, R5). It holds the trigger, the
/// gap kind, the outcome and the time, all closed words and a timestamp: not which signals, not how many readings,
/// not a value. Diagnostics reads <see cref="LastOutcome"/> for the <c>BodySync</c> component, and
/// <see cref="Last"/> says whether the background gap is why it is only partial.
/// </summary>
public sealed class BodySyncStatusFile : IBodySyncStatus
{
    private static readonly object FileLock = new();

    private readonly string path;
    private readonly Func<DateTimeOffset> clock;

    public BodySyncStatusFile(string path, Func<DateTimeOffset> clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(clock);
        this.path = path;
        this.clock = clock;
    }

    public void Record(BodySyncResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("trigger", result.Trigger.ToString());
            writer.WriteString("gap", result.Gap.ToString());
            writer.WriteString("outcome", result.Outcome.ToString());
            writer.WriteString("at", clock().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        lock (FileLock)
        {
            AtomicFile.WriteAllText(path, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }
    }

    /// <summary>The last sync recorded, or null when none was, or when the file cannot be read.</summary>
    public BodySyncRecord? Last()
    {
        lock (FileLock)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && Enum.TryParse<BodySyncTrigger>(Text(root, "trigger"), out var trigger) && Enum.IsDefined(trigger)
                    && Enum.TryParse<BodySyncGap>(Text(root, "gap"), out var gap) && Enum.IsDefined(gap)
                    && Enum.TryParse<DiagnosticsOutcome>(Text(root, "outcome"), out var outcome) && Enum.IsDefined(outcome)
                    && DateTimeOffset.TryParse(Text(root, "at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                {
                    return new BodySyncRecord(trigger, gap, outcome, at);
                }

                return null;
            }
            catch (JsonException)
            {
                // A damaged status says nothing; Diagnostics then reports that Body sync has not run.
                return null;
            }
        }
    }

    /// <summary>The last outcome, or <see cref="DiagnosticsOutcome.NeverRan"/> when there is none.</summary>
    public DiagnosticsOutcome LastOutcome() => Last()?.Outcome ?? DiagnosticsOutcome.NeverRan;

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
