using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codex.Domain.Apps;

/// <summary>
/// The wire contract Flyleaf relays playback sessions to Pulp over (P12 Release F, finding 2.3, D30,
/// D182, D288). Flyleaf is already an enabled notification listener on the locked phone, which makes
/// it the one app allowed to read other apps' media sessions; it forwards <i>package, title, position,
/// duration, state</i> for the packages Pulp named and no others.
///
/// An <b>explicit</b> broadcast naming Pulp's receiver, under a signature-level permission Pulp
/// declares: naming the component means nothing else can read what is being watched, and the
/// permission means nothing else can feed Pulp invented progress. The payload crosses as exact text,
/// one JSON object under <see cref="SessionExtra"/>. Pulp's <c>SessionContractTests</c> reads this file
/// by content, so neither half can move alone.
/// </summary>
public static class PlaybackSessionHandoff
{
    public const string Package = "com.storyworthtelling.pulp";

    public const string Receiver = "com.storyworthtelling.pulp.SessionReceiver";

    public const string Action = "com.storyworthtelling.pulp.action.PLAYBACK_SESSION";

    /// <summary>Declared by Pulp at protectionLevel="signature"; Flyleaf must hold it to send.</summary>
    public const string Permission = "com.storyworthtelling.pulp.RECEIVE_SESSIONS";

    public const string SessionExtra = "session";

    public const string FieldPackage = "package";

    public const string FieldTitle = "title";

    public const string FieldPositionMs = "positionMs";

    public const string FieldDurationMs = "durationMs";

    public const string FieldState = "state";

    public const string StatePlaying = "playing";

    public const string StatePaused = "paused";

    public const string StateStopped = "stopped";

    /// <summary>A title is a label, not a document: longer ones are cut before they leave the phone.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// How far a playing position must move from the last one sent before another goes out. A position
    /// ticks every second; Pulp wants "where it got to", not a heartbeat. <b>Un-evidenced:</b> no
    /// service's session has been read on a device yet (finding section 4); the value to tune.
    /// </summary>
    public const long ProgressStepMs = 30000;
}

public enum PlaybackSessionState
{
    Playing,
    Paused,
    Stopped,
}

/// <summary>One reading of a playing app's media session. Position and duration are milliseconds; a duration of zero means the app did not publish one.</summary>
public sealed record PlaybackSessionReading(
    string Package,
    string? Title,
    long PositionMs,
    long DurationMs,
    PlaybackSessionState State);

/// <summary>The text form of a <see cref="PlaybackSessionReading"/>: one JSON object.</summary>
public static class PlaybackSessionCodec
{
    private static readonly Regex PackageShape = new(
        @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.Compiled);

    public static string Encode(PlaybackSessionReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [PlaybackSessionHandoff.FieldPackage] = reading.Package,
        };
        if (reading.Title is { Length: > 0 } title)
        {
            fields[PlaybackSessionHandoff.FieldTitle] = title;
        }

        fields[PlaybackSessionHandoff.FieldPositionMs] = reading.PositionMs;
        fields[PlaybackSessionHandoff.FieldDurationMs] = reading.DurationMs;
        fields[PlaybackSessionHandoff.FieldState] = StateText(reading.State);
        return JsonSerializer.Serialize(fields);
    }

    /// <summary>True only for a whole, plausible message; anything else is ignored rather than guessed at.</summary>
    public static bool TryDecode(string? text, out PlaybackSessionReading reading)
    {
        reading = new PlaybackSessionReading(string.Empty, null, 0, 0, PlaybackSessionState.Stopped);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryString(root, PlaybackSessionHandoff.FieldPackage, out var package)
                || !PackageShape.IsMatch(package!)
                || !TryNonNegative(root, PlaybackSessionHandoff.FieldPositionMs, out var position)
                || !TryNonNegative(root, PlaybackSessionHandoff.FieldDurationMs, out var duration)
                || !TryString(root, PlaybackSessionHandoff.FieldState, out var stateText)
                || !TryState(stateText!, out var state))
            {
                return false;
            }

            string? title = null;
            if (root.TryGetProperty(PlaybackSessionHandoff.FieldTitle, out var titleElement))
            {
                if (titleElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                title = titleElement.GetString();
            }

            reading = new PlaybackSessionReading(package!, title, position, duration, state);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string StateText(PlaybackSessionState state) => state switch
    {
        PlaybackSessionState.Playing => PlaybackSessionHandoff.StatePlaying,
        PlaybackSessionState.Paused => PlaybackSessionHandoff.StatePaused,
        _ => PlaybackSessionHandoff.StateStopped,
    };

    private static bool TryState(string text, out PlaybackSessionState state)
    {
        switch (text)
        {
            case PlaybackSessionHandoff.StatePlaying:
                state = PlaybackSessionState.Playing;
                return true;
            case PlaybackSessionHandoff.StatePaused:
                state = PlaybackSessionState.Paused;
                return true;
            case PlaybackSessionHandoff.StateStopped:
                state = PlaybackSessionState.Stopped;
                return true;
            default:
                state = PlaybackSessionState.Stopped;
                return false;
        }
    }

    private static bool TryString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return value is not null;
    }

    private static bool TryNonNegative(JsonElement root, string name, out long value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out value)
            && value >= 0
            && value <= TimeSpan.FromDays(7).TotalMilliseconds;
    }
}

/// <summary>
/// Decides which readings leave the phone. Only a package in the set Pulp named (the stored
/// <see cref="HiddenApps"/> set: Pulp names exactly the streaming services the household ticked, which
/// are the ones whose playback it tracks, so a second stored set would be the same list twice) is ever
/// relayed, and only its title travels, trimmed and cut. Per package it then sends only <i>changes</i>:
/// a first reading, a different state, title or duration, or a position that moved a whole
/// <see cref="PlaybackSessionHandoff.ProgressStepMs"/> from the last one sent (either way, so a seek
/// counts). Not thread-safe; one watcher owns one relay.
/// </summary>
public sealed class PlaybackSessionRelay
{
    private readonly Dictionary<string, PlaybackSessionReading> lastSent = new(StringComparer.Ordinal);

    /// <summary>The reading to send, or null when it should not be sent.</summary>
    public PlaybackSessionReading? Offer(PlaybackSessionReading reading, HiddenApps named)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(named);
        if (!named.Contains(reading.Package))
        {
            return null;
        }

        var title = reading.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            title = null;
        }
        else if (title.Length > PlaybackSessionHandoff.MaxTitleLength)
        {
            title = title[..PlaybackSessionHandoff.MaxTitleLength];
        }

        var candidate = reading with { Title = title };
        if (lastSent.TryGetValue(candidate.Package, out var previous) && !Differs(previous, candidate))
        {
            return null;
        }

        lastSent[candidate.Package] = candidate;
        return candidate;
    }

    /// <summary>Drops a package's memory so its next session is sent from the start.</summary>
    public void Forget(string package) => lastSent.Remove(package);

    private static bool Differs(PlaybackSessionReading previous, PlaybackSessionReading next) =>
        previous.State != next.State
        || previous.DurationMs != next.DurationMs
        || !string.Equals(previous.Title, next.Title, StringComparison.Ordinal)
        || Math.Abs(next.PositionMs - previous.PositionMs) >= PlaybackSessionHandoff.ProgressStepMs;
}
