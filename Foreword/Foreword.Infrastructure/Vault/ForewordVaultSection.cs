using Storyworthtelling.Voice.Secrets;

namespace Foreword.Infrastructure.Vault;

/// <summary>
/// What Foreword writes into the shared vault, under the suite's rule (<see cref="VaultMerge.Pick"/>): each value
/// replaces the vault's only when the owner just changed it here, and never as a blank. The shared section's
/// calendar mapping is left as it is (it moved to each account's personal vault). Pure, so the Android head's
/// sync is a thin caller.
/// </summary>
public static class ForewordVaultSection
{
    public const string NytCookie = "nyt-cookie";
    public const string NytApiKey = "nyt-api-key";
    public const string Remarkable = "remarkable";
    public const string Anthropic = "anthropic";
    public const string MapsKey = "maps-key";

    /// <summary>The household's address and point, changed together.</summary>
    public const string Home = "home";

    /// <summary>This install's values, as its credential store holds them.</summary>
    public sealed record Local(
        string NytSessionCookie,
        string NytApiKey,
        string RemarkableDeviceToken,
        string AnthropicApiKey,
        string MapsApiKey,
        string HomeAddress,
        double HomeLatitude,
        double HomeLongitude);

    public static SecretsVault Merge(
        SecretsVault vault, Local local, IReadOnlyCollection<string> changed, IReadOnlyDictionary<string, string> capturedFileIds)
    {
        var home = changed.Contains(Home);
        return vault with
        {
            Nyt = vault.Nyt with
            {
                SessionCookie = VaultMerge.Pick(local.NytSessionCookie, vault.Nyt.SessionCookie, changed.Contains(NytCookie)),
                ApiKey = VaultMerge.Pick(local.NytApiKey, vault.Nyt.ApiKey, changed.Contains(NytApiKey)),
            },
            Remarkable = vault.Remarkable with
            {
                DeviceToken = VaultMerge.Pick(local.RemarkableDeviceToken, vault.Remarkable.DeviceToken, changed.Contains(Remarkable)),
            },
            Foreword = vault.Foreword with
            {
                AnthropicApiKey = VaultMerge.Pick(local.AnthropicApiKey, vault.Foreword.AnthropicApiKey, changed.Contains(Anthropic)),
            },
            // The Maps key is shared rather than Foreword's own: it bills a project, and the house beside it is
            // the household's address, not this app's setting.
            Maps = vault.Maps with
            {
                ApiKey = VaultMerge.Pick(local.MapsApiKey, vault.Maps.ApiKey, changed.Contains(MapsKey)),
                HomeAddress = VaultMerge.Pick(local.HomeAddress, vault.Maps.HomeAddress, home),
                HomeLatitude = VaultMerge.Pick(local.HomeLatitude, vault.Maps.HomeLatitude, home),
                HomeLongitude = VaultMerge.Pick(local.HomeLongitude, vault.Maps.HomeLongitude, home),
            },
            DriveFileIds = VaultMerge.MergeDriveFileIds(vault.DriveFileIds, capturedFileIds),
        };
    }
}
