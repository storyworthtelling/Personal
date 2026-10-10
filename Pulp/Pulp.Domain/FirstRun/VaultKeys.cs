namespace Pulp.Domain.FirstRun;

/// <summary>One of Pulp's keys as the Vault page lists it: the credential id its health is kept under, and its name.</summary>
public sealed record VaultKey(string CredentialId, string Name);

/// <summary>
/// The three household keys the Vault page shows (D142, D285), how what the owner types merges into
/// the vault's Pulp section, and when a rejection still stands. The ids are the ones the clients report
/// health under (<c>PulpCredentialIds</c>), restated here because the Domain references nothing; a test
/// holds the two together.
/// </summary>
public static class VaultKeys
{
    public static IReadOnlyList<VaultKey> All { get; } =
    [
        new("streaming-availability", "Streaming Availability"),
        new("omdb", "OMDb"),
        new("youtube", "YouTube"),
    ];

    /// <summary>One plain sentence for a key its service refused. Never the status code or the service's own words.</summary>
    public static string Rejected(string name) =>
        $"The {name} service turned this key down. Paste a new one, then Save.";

    /// <summary>
    /// The key to write: the entry when the owner typed one, otherwise the key the vault already holds
    /// — a working key's field is hidden, so a blank entry means "leave it", never "clear it".
    /// </summary>
    public static string Merge(string current, string? entered) =>
        string.IsNullOrWhiteSpace(entered) ? current : entered.Trim();

    /// <summary>
    /// Whether a key's last rejection still applies. Health is written only by a live call, so a key
    /// replaced after it was turned down is not rejected any more — it is simply not checked yet.
    /// </summary>
    public static bool StillRejected(bool failing, DateTimeOffset? checkedAt, DateTimeOffset? replacedAt) =>
        failing && (replacedAt is not { } replaced || checkedAt is not { } check || check > replaced);
}
