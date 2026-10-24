using System.Globalization;
using System.Text.Json;
using Constitution.Domain.Diagnostics;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// Remembers how the last read-back ended, for the Diagnostics export's "Read-back" line: one closed word
/// (<see cref="DiagnosticsOutcome"/>) and a time. Not which documents, not how many marks, not a word from a page.
/// </summary>
public sealed class ReadBackStatusFile
{
    private static readonly object FileLock = new();

    private readonly string path;
    private readonly Func<DateTimeOffset> clock;

    public ReadBackStatusFile(string path, Func<DateTimeOffset> clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(clock);
        this.path = path;
        this.clock = clock;
    }

    public void Record(DiagnosticsOutcome outcome)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "That is not an outcome Diagnostics knows.");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("outcome", ReadBackWireNames.Of(outcome));
            writer.WriteString("at", clock().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        lock (FileLock)
        {
            AtomicFile.WriteAllText(path, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }
    }

    /// <summary>The last outcome recorded, or <see cref="DiagnosticsOutcome.NeverRan"/> when there is none or the file cannot be read.</summary>
    public DiagnosticsOutcome LastOutcome()
    {
        lock (FileLock)
        {
            if (!File.Exists(path))
            {
                return DiagnosticsOutcome.NeverRan;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("outcome", out var word) && word.ValueKind == JsonValueKind.String
                    && ReadBackWireNames.DiagnosticsOutcomeOf(word.GetString()) is { } outcome)
                {
                    return outcome;
                }

                return DiagnosticsOutcome.NeverRan;
            }
            catch (JsonException)
            {
                // A damaged status says nothing; Diagnostics then reports that the read-back has not run.
                return DiagnosticsOutcome.NeverRan;
            }
        }
    }
}
