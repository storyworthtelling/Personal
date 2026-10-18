using System.Text.Json;
using Constitution.Domain.Body;

namespace Constitution.Domain.Health;

/// <summary>Where one signal stands with this person.</summary>
public enum SignalChoice
{
    /// <summary>The chart has not been opened, so nothing was asked.</summary>
    NotAsked,

    /// <summary>It was asked for and not given, or given and since withdrawn.</summary>
    Asked,

    /// <summary>It was given.</summary>
    Granted,
}

/// <summary>
/// Which Health Connect signals this person has been asked for and has given (C-7.1.2), immutable. It is
/// what the personal vault carries, so a phone that has just been reset knows which charts to ask about
/// again, rather than discovering it chart by chart. The platform, not this record, is the truth about what
/// may be read right now (<see cref="IHealthRecordSource.IsGranted"/>); this records what was said.
///
/// <para>The JSON is signals and states and nothing else: no reading, no date, no name. Anything it cannot
/// read, from a newer app or a damaged file, reads as nothing chosen rather than throwing, because a
/// choice list that stops the app opening would be a worse failure than asking once more.</para>
/// </summary>
public sealed class HealthChoices
{
    private const int Version = 1;

    private readonly IReadOnlyDictionary<BodySignal, SignalChoice> choices;

    private HealthChoices(IReadOnlyDictionary<BodySignal, SignalChoice> choices)
    {
        this.choices = choices;
    }

    /// <summary>Nothing asked of any signal.</summary>
    public static HealthChoices Empty { get; } = new(new Dictionary<BodySignal, SignalChoice>());

    /// <summary>The signal's state; <see cref="SignalChoice.NotAsked"/> for one never recorded or not read from Health Connect.</summary>
    public SignalChoice Of(BodySignal signal) => choices.GetValueOrDefault(signal, SignalChoice.NotAsked);

    /// <summary>A copy with one signal's state changed.</summary>
    /// <exception cref="ArgumentException"><paramref name="signal"/> is not one Health Connect supplies.</exception>
    public HealthChoices With(BodySignal signal, SignalChoice choice)
    {
        if (!BodySignals.HealthConnect.Contains(signal))
        {
            throw new ArgumentException($"{signal} is not read from Health Connect, so there is nothing to choose.", nameof(signal));
        }

        var next = new Dictionary<BodySignal, SignalChoice>(choices);
        if (choice == SignalChoice.NotAsked)
        {
            next.Remove(signal);
        }
        else
        {
            next[signal] = choice;
        }

        return new HealthChoices(next);
    }

    /// <summary>
    /// A copy brought into line with the platform: what it grants is <see cref="SignalChoice.Granted"/>, what was
    /// granted and is no longer becomes <see cref="SignalChoice.Asked"/> (it was withdrawn, so the chart should
    /// ask again, not behave as though it never had), and everything else stays as it was.
    /// </summary>
    public HealthChoices Reconciled(Func<BodySignal, bool> isGranted)
    {
        ArgumentNullException.ThrowIfNull(isGranted);

        var next = this;
        foreach (var signal in BodySignals.HealthConnect)
        {
            if (isGranted(signal))
            {
                next = next.With(signal, SignalChoice.Granted);
            }
            else if (next.Of(signal) == SignalChoice.Granted)
            {
                next = next.With(signal, SignalChoice.Asked);
            }
        }

        return next;
    }

    /// <summary>The JSON the personal vault carries, signals in Health Connect order.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartObject("signals");
            foreach (var signal in BodySignals.HealthConnect)
            {
                if (Of(signal) is var state && state != SignalChoice.NotAsked)
                {
                    writer.WriteString(BodySignals.Id(signal), state == SignalChoice.Granted ? "granted" : "asked");
                }
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads the JSON; anything unreadable is <see cref="Empty"/>, and an unknown signal or state is skipped.</summary>
    public static HealthChoices FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("signals", out var signals)
                || signals.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var read = Empty;
            foreach (var entry in signals.EnumerateObject())
            {
                if (BodySignals.TryFromId(entry.Name, out var signal)
                    && BodySignals.HealthConnect.Contains(signal)
                    && entry.Value.ValueKind == JsonValueKind.String
                    && entry.Value.GetString() is { } word
                    && StateOf(word) is { } state)
                {
                    read = read.With(signal, state);
                }
            }

            return read;
        }
        catch (JsonException)
        {
            // Damaged or foreign JSON is not an error the owner can act on: ask again instead.
            return Empty;
        }
    }

    private static SignalChoice? StateOf(string word) => word switch
    {
        "granted" => SignalChoice.Granted,
        "asked" => SignalChoice.Asked,
        _ => null,
    };
}
