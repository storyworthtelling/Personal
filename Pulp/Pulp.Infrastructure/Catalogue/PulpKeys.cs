using Storyworthtelling.Voice.Secrets;

namespace Pulp.Infrastructure.Catalogue;

/// <summary>
/// Where the clients read Pulp's three household keys from, live on every call — so a key the vault
/// restores, or the owner replaces in Settings, is used by the next request without rebuilding a
/// client. The head implements it over the vault's <see cref="PulpSecrets"/> section.
/// </summary>
public interface IPulpKeySource
{
    PulpSecrets Keys { get; }
}

/// <summary>The credential ids the clients report health under, shared with the settings screen.</summary>
public static class PulpCredentialIds
{
    public const string StreamingAvailability = "streaming-availability";

    public const string Omdb = "omdb";

    public const string YouTube = "youtube";
}
