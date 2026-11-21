using System.Text.Json;

namespace Watch.Domain.Updates;

/// <summary>One install session the installer began for an app: the build it carries, its id, and when it began.</summary>
public sealed record InstallSession(string Package, long VersionCode, int SessionId, DateTimeOffset Began);

/// <summary>The installer's record of its sessions: each app's current one, and the ids it abandoned itself.</summary>
public sealed record InstallSessionRecord(IReadOnlyList<InstallSession> Current, IReadOnlyList<int> Abandoned)
{
    public static readonly InstallSessionRecord Empty = new([], []);
}

/// <summary>What the installer does with a status the platform sends it for one session.</summary>
public enum SessionVerdict
{
    /// <summary>The app's current session (or one the record cannot place): its status is the build's outcome.</summary>
    Settle,

    /// <summary>A session the installer abandoned itself, or one that is no longer the app's current session: its end
    /// says nothing about the build waiting on the wrist, so nothing is recorded and the current card stays.</summary>
    Ignore,

    /// <summary>The current session, ended aborted once the platform's own limit on a waiting session had passed:
    /// nobody turned it down, so it is not a decline. The card goes and the build stays waiting, to be offered again.</summary>
    LetGo,
}

/// <summary>
/// P14 release A fix round, finding 5. A newer send abandons the session still waiting for the app (one prompt per app),
/// and the platform then reports that abandoned session as aborted; read as the app's outcome, that was a decline the
/// person never made, recorded over the new build's waiting result and clearing the new build's card. And a waiting
/// session the platform lets go after its limit came back aborted too, a decline that was never sent again. Each status
/// is now judged by the session it is for.
/// </summary>
public static class InstallSessions
{
    /// <summary>How many abandoned session ids are kept: their statuses come within moments of abandoning.</summary>
    public const int AbandonedKept = 32;

    /// <summary>
    /// The platform's limit on a session nobody answers: Android's package installer drops a session three days after it
    /// was made (<c>PackageInstallerService</c>'s <c>MAX_AGE_MILLIS</c>, read from AOSP, not yet seen on a watch). It is
    /// the phone's own wait before offering a waiting build again (<see cref="WatchSendPlan.WaitingOffer"/>).
    /// </summary>
    public static readonly TimeSpan PlatformLetsGoAfter = TimeSpan.FromDays(3);

    /// <summary>An abort this close to the limit is the platform's too: the session began a moment after the installer
    /// noted it, and the two read the clock apart.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromHours(1);

    // Stored words, never shown or sent.
    private const int Version = 1;
    private const string VersionField = "v";
    private const string CurrentField = "current";
    private const string AbandonedField = "abandoned";
    private const string PackageField = "package";
    private const string VersionCodeField = "versionCode";
    private const string SessionField = "session";
    private const string BeganField = "began";

    /// <summary>The record once this installer has begun <paramref name="session"/>: it is the app's current session.</summary>
    public static InstallSessionRecord Began(InstallSessionRecord record, InstallSession session) =>
        record with
        {
            Current = [.. record.Current.Where(entry => !string.Equals(entry.Package, session.Package, StringComparison.Ordinal)), session],
        };

    /// <summary>The record once this installer is about to abandon <paramref name="sessionId"/>: its status, when it
    /// comes, is ignored. The newest <see cref="AbandonedKept"/> are kept.</summary>
    public static InstallSessionRecord Abandoning(InstallSessionRecord record, int sessionId)
    {
        var abandoned = record.Abandoned.Where(id => id != sessionId).Append(sessionId).ToList();
        return new InstallSessionRecord(
            [.. record.Current.Where(entry => entry.SessionId != sessionId)],
            [.. abandoned.Skip(Math.Max(0, abandoned.Count - AbandonedKept))]);
    }

    /// <summary>The record once an app's session has settled: that session is no longer current.</summary>
    public static InstallSessionRecord Ended(InstallSessionRecord record, string package, int sessionId) =>
        record with
        {
            Current = [.. record.Current.Where(entry => !(string.Equals(entry.Package, package, StringComparison.Ordinal) && entry.SessionId == sessionId))],
        };

    /// <param name="sessionId">The session the status is for, or null when the intent carried none (one made by an
    /// installer before this fix): that is settled as it always was.</param>
    public static SessionVerdict Judge(InstallSessionRecord record, string package, int? sessionId, InstallerStatus status, DateTimeOffset now)
    {
        if (sessionId is not { } id)
        {
            return SessionVerdict.Settle;
        }
        if (record.Abandoned.Contains(id))
        {
            return SessionVerdict.Ignore;
        }

        var current = record.Current.FirstOrDefault(entry => string.Equals(entry.Package, package, StringComparison.Ordinal));
        if (current is null)
        {
            // Nothing recorded for the app (a record from before the fix, or one that would not read): as before.
            return SessionVerdict.Settle;
        }
        if (current.SessionId != id)
        {
            return SessionVerdict.Ignore;
        }

        return status == InstallerStatus.Aborted && now - current.Began >= PlatformLetsGoAfter - Slack
            ? SessionVerdict.LetGo
            : SessionVerdict.Settle;
    }

    public static string Encode(InstallSessionRecord record)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionField, Version);
            writer.WriteStartArray(CurrentField);
            foreach (var session in record.Current)
            {
                writer.WriteStartObject();
                writer.WriteString(PackageField, session.Package);
                writer.WriteNumber(VersionCodeField, session.VersionCode);
                writer.WriteNumber(SessionField, session.SessionId);
                writer.WriteString(BeganField, session.Began);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray(AbandonedField);
            foreach (var id in record.Abandoned)
            {
                writer.WriteNumberValue(id);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(json.ToArray());
    }

    /// <summary>The stored record, or none: a record that will not read is settled as before, never refused.</summary>
    public static InstallSessionRecord Decode(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return InstallSessionRecord.Empty;
        }
        try
        {
            using var document = JsonDocument.Parse(stored);
            return Read(document.RootElement) ?? InstallSessionRecord.Empty;
        }
        catch (JsonException)
        {
            // Damaged on the watch: statuses are settled as they were before the record existed.
            return InstallSessionRecord.Empty;
        }
    }

    private static InstallSessionRecord? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(VersionField, out var version) || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var v) || v != Version
            || !root.TryGetProperty(CurrentField, out var current) || current.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty(AbandonedField, out var abandoned) || abandoned.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var sessions = new List<InstallSession>();
        foreach (var entry in current.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty(PackageField, out var package) || package.ValueKind != JsonValueKind.String
                || package.GetString() is not { Length: > 0 } name
                || !entry.TryGetProperty(VersionCodeField, out var code) || code.ValueKind != JsonValueKind.Number
                || !code.TryGetInt64(out var versionCode)
                || !entry.TryGetProperty(SessionField, out var session) || session.ValueKind != JsonValueKind.Number
                || !session.TryGetInt32(out var sessionId)
                || !entry.TryGetProperty(BeganField, out var began) || began.ValueKind != JsonValueKind.String
                || !began.TryGetDateTimeOffset(out var when))
            {
                return null;
            }
            sessions.Add(new InstallSession(name, versionCode, sessionId, when));
        }

        var ids = new List<int>();
        foreach (var entry in abandoned.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Number || !entry.TryGetInt32(out var id))
            {
                return null;
            }
            ids.Add(id);
        }
        return new InstallSessionRecord(sessions, ids);
    }
}

/// <summary>The decided action's word in the installer's status intent: a wire value, frozen, since a pending intent
/// outlives the process (and the installer's own update) that made it.</summary>
public static class WatchUpdateActions
{
    private static readonly (WatchUpdateAction Action, string Word)[] Words =
    [
        (WatchUpdateAction.Refuse, "refuse"),
        (WatchUpdateAction.FirstInstall, "first-install"),
        (WatchUpdateAction.AskedUpdate, "asked-update"),
        (WatchUpdateAction.SilentUpdate, "silent-update"),
    ];

    public static string Word(WatchUpdateAction action) => Words.Single(pair => pair.Action == action).Word;

    /// <summary>The action a word names, exactly as written; anything else is none.</summary>
    public static WatchUpdateAction? FromWord(string? word) =>
        Words.Where(pair => string.Equals(pair.Word, word, StringComparison.Ordinal)).Select(pair => (WatchUpdateAction?)pair.Action).FirstOrDefault();
}
