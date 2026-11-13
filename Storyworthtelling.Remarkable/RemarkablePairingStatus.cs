using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Remarkable;

/// <summary>
/// What a settings screen says about the reMarkable pairing (audit 11 §11). A stored token is not a working pairing:
/// the reMarkable cloud can revoke it, and every head records that under <see cref="RemarkableCredentialId"/>. So
/// the line follows the pairing's health, not the token's presence.
/// </summary>
/// <param name="Line">The status sentence.</param>
/// <param name="Button">The pairing button's label.</param>
/// <param name="NeedsPairing">Whether the code entry should be open.</param>
public sealed record RemarkablePairingStatus(string Line, string Button, bool NeedsPairing)
{
    /// <param name="notPaired">The app's own sentence for a phone that has never paired.</param>
    public static RemarkablePairingStatus Describe(bool paired, CredentialHealth health, string notPaired)
    {
        if (!paired)
        {
            return new RemarkablePairingStatus(notPaired, "Pair", NeedsPairing: true);
        }

        return health.Status switch
        {
            CredentialStatus.Failing => new RemarkablePairingStatus(
                "The reMarkable turned this phone's pairing away. Pair again.", "Pair again", NeedsPairing: true),
            CredentialStatus.Ok => new RemarkablePairingStatus("Paired ✓", "Re-pair", NeedsPairing: false),
            _ => new RemarkablePairingStatus("Paired", "Re-pair", NeedsPairing: false),
        };
    }
}
