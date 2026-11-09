using Storyworthtelling.Voice.Secrets;

namespace Folio.Infrastructure.Vault;

/// <summary>
/// What Folio writes into the shared vault, under the suite's rule (<see cref="VaultMerge.Pick"/>): its Claude
/// and Gemini keys and the reMarkable token, each replacing the vault's only when the owner just changed it here,
/// and never as a blank. Pure, so the Android head's sync is a thin caller.
/// </summary>
public static class FolioVaultSection
{
    public const string Claude = "claude";
    public const string Gemini = "gemini";
    public const string Remarkable = "remarkable";

    /// <summary>This install's values, as Settings holds them.</summary>
    public sealed record Local(string ClaudeApiKey, string GeminiApiKey, string RemarkableDeviceToken);

    public static SecretsVault Merge(
        SecretsVault vault, Local local, IReadOnlyCollection<string> changed, IReadOnlyDictionary<string, string> capturedFileIds) =>
        vault with
        {
            Claude = vault.Claude with { ApiKey = VaultMerge.Pick(local.ClaudeApiKey, vault.Claude.ApiKey, changed.Contains(Claude)) },
            Gemini = vault.Gemini with { ApiKey = VaultMerge.Pick(local.GeminiApiKey, vault.Gemini.ApiKey, changed.Contains(Gemini)) },
            Remarkable = vault.Remarkable with
            {
                DeviceToken = VaultMerge.Pick(local.RemarkableDeviceToken, vault.Remarkable.DeviceToken, changed.Contains(Remarkable)),
            },
            DriveFileIds = VaultMerge.MergeDriveFileIds(vault.DriveFileIds, capturedFileIds),
        };
}
