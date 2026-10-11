using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pulp.Domain.Sessions;

/// <summary>
/// What Flyleaf relays to Pulp about a playing session (finding 2.3, D30, D182, D288). The wire names
/// are copied byte-for-byte from <c>FocusPhone/Codex.Domain/Apps/PlaybackSessions.cs</c>
/// (<c>PlaybackSessionHandoff</c>); <c>SessionContractTests</c> (guard G18) reads that file by content,
/// so a drift on either side fails there instead of dropping the broadcast silently.
/// </summary>
public static class SessionHandOff
{
    public const string Package = PulpIdentity.ApplicationId;

    public const string Receiver = PulpIdentity.ApplicationId + ".SessionReceiver";

    public const string Action = PulpIdentity.ApplicationId + ".action.PLAYBACK_SESSION";

    /// <summary>Declared by Pulp at signature level; only a signed sibling (Flyleaf) can send.</summary>
    public const string Permission = PulpIdentity.ApplicationId + ".RECEIVE_SESSIONS";

    public const string SessionExtra = "session";

    public const string FieldPackage = "package";

    public const string FieldTitle = "title";

    public const string FieldPositionMs = "positionMs";

    public const string FieldDurationMs = "durationMs";

    public const string FieldState = "state";

    public const string StatePlaying = "playing";

    public const string StatePaused = "paused";

    public const string StateStopped = "stopped";
}

public enum SessionPlayState
{
    Playing,
    Paused,
    Stopped,
}

/// <summary>One relayed reading: which app, what it said it was playing (when it said), where, and how long.</summary>
public sealed record SessionMessage(
    string Package,
    string? Title,
    TimeSpan Position,
    TimeSpan Duration,
    SessionPlayState State);

/// <summary>Reads the relayed text. Garbled, partial or implausible text is ignored, never guessed at.</summary>
public static class SessionDecoder
{
    private static readonly Regex PackageShape = new(
        @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.Compiled);

    private static readonly long LongestMs = (long)TimeSpan.FromDays(7).TotalMilliseconds;

    public static bool TryDecode(string? text, out SessionMessage message)
    {
        message = new SessionMessage(string.Empty, null, TimeSpan.Zero, TimeSpan.Zero, SessionPlayState.Stopped);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryString(root, SessionHandOff.FieldPackage, out var package)
                || !PackageShape.IsMatch(package!)
                || !TryMilliseconds(root, SessionHandOff.FieldPositionMs, out var position)
                || !TryMilliseconds(root, SessionHandOff.FieldDurationMs, out var duration)
                || !TryString(root, SessionHandOff.FieldState, out var stateText)
                || !TryState(stateText!, out var state))
            {
                return false;
            }

            string? title = null;
            if (root.TryGetProperty(SessionHandOff.FieldTitle, out var titleElement))
            {
                if (titleElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                title = titleElement.GetString()?.Trim();
                if (string.IsNullOrEmpty(title))
                {
                    title = null;
                }
            }

            message = new SessionMessage(package!, title, position, duration, state);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryState(string text, out SessionPlayState state)
    {
        switch (text)
        {
            case SessionHandOff.StatePlaying:
                state = SessionPlayState.Playing;
                return true;
            case SessionHandOff.StatePaused:
                state = SessionPlayState.Paused;
                return true;
            case SessionHandOff.StateStopped:
                state = SessionPlayState.Stopped;
                return true;
            default:
                state = SessionPlayState.Stopped;
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

    private static bool TryMilliseconds(JsonElement root, string name, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (!root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var milliseconds)
            || milliseconds < 0
            || milliseconds > LongestMs)
        {
            return false;
        }

        value = TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }
}
