using System.Text.Json;
using System.Text.Json.Serialization;

namespace Storyworthtelling.Voice.Health;

// The last-known outcome of using a credential. Because the vault now backs up and restores every key,
// a settings screen no longer needs to show a filled-in key as an editable field — it can show that the
// key is working and only reveal the entry when the key is missing or a live call with it just failed.
// This health record is what makes that call: each app's worker marks a credential Ok on a successful
// authenticated call and Failing on an auth/credential error (not on a transient network blip), and the
// settings screen reads it back through CredentialFieldState.

public enum CredentialStatus
{
    // Never exercised (freshly entered, or restored from the vault but not yet used).
    Unknown,
    // The most recent authenticated call using this credential succeeded.
    Ok,
    // The most recent authenticated call rejected this credential (bad key / expired token / revoked).
    Failing
}

public sealed record CredentialHealth(CredentialStatus Status, string Message, DateTimeOffset? CheckedAtUtc)
{
    public static readonly CredentialHealth Unknown = new(CredentialStatus.Unknown, string.Empty, null);

    public static CredentialHealth Ok(DateTimeOffset checkedAtUtc) =>
        new(CredentialStatus.Ok, string.Empty, checkedAtUtc);

    public static CredentialHealth Failing(string message, DateTimeOffset checkedAtUtc) =>
        new(CredentialStatus.Failing, message ?? string.Empty, checkedAtUtc);
}

// An immutable per-app map of credential id → health. Ids are stable, app-chosen strings
// (e.g. "claude", "gemini", "plaid", "institution:<itemId>").
public sealed class CredentialHealthReport
{
    private readonly IReadOnlyDictionary<string, CredentialHealth> entries;

    public static readonly CredentialHealthReport Empty =
        new(new Dictionary<string, CredentialHealth>());

    public CredentialHealthReport(IReadOnlyDictionary<string, CredentialHealth> entries)
    {
        this.entries = entries;
    }

    public IEnumerable<string> Ids => entries.Keys;

    public CredentialHealth Get(string id) =>
        entries.TryGetValue(id, out var health) ? health : CredentialHealth.Unknown;

    public CredentialHealthReport MarkOk(string id, DateTimeOffset checkedAtUtc) =>
        With(id, CredentialHealth.Ok(checkedAtUtc));

    public CredentialHealthReport MarkFailing(string id, string message, DateTimeOffset checkedAtUtc) =>
        With(id, CredentialHealth.Failing(message, checkedAtUtc));

    public CredentialHealthReport With(string id, CredentialHealth health)
    {
        var next = new Dictionary<string, CredentialHealth>(entries) { [id] = health };
        return new CredentialHealthReport(next);
    }
}

// Persists a CredentialHealthReport as JSON so a device store (Preferences) can round-trip it. Kept
// separate from the model so the model stays a plain record with no serialization attributes on it.
public static class CredentialHealthCodec
{
    private sealed record Entry(
        [property: JsonPropertyName("status")] CredentialStatus Status,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("checkedAtUtc")] DateTimeOffset? CheckedAtUtc);

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(CredentialHealthReport report)
    {
        var map = report.Ids.ToDictionary(
            id => id,
            id =>
            {
                var h = report.Get(id);
                return new Entry(h.Status, h.Message, h.CheckedAtUtc);
            });
        return JsonSerializer.Serialize(map, Options);
    }

    public static CredentialHealthReport Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return CredentialHealthReport.Empty;

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json, Options);
            if (map is null)
                return CredentialHealthReport.Empty;

            var entries = map.ToDictionary(
                kv => kv.Key,
                kv => new CredentialHealth(kv.Value.Status, kv.Value.Message ?? string.Empty, kv.Value.CheckedAtUtc));
            return new CredentialHealthReport(entries);
        }
        catch (JsonException)
        {
            // A corrupt blob is treated as "nothing known yet" — the app still works, it just shows
            // every field as it would on a fresh install.
            return CredentialHealthReport.Empty;
        }
    }
}
