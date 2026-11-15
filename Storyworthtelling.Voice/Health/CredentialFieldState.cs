namespace Storyworthtelling.Voice.Health;

// The pure decision a settings screen makes for one credential field: given whether a value is stored
// and its last-known health, should the entry be shown at all, and what status line goes with it. This
// is the whole point of health tracking — a working key is a "done" state, not something to keep on
// screen and re-type. The entry appears only when there's something to do: no value yet, or a live call
// with the current value just failed. Otherwise the credential does not appear at all (owner's ruling,
// audit W8.4b): no "Working" line and no Replace, since a setting that needs no work is noise.
public sealed record CredentialFieldState(bool ShowEntry, string StatusText, bool IsWarning)
{
    /// <summary>Whether the credential is on the screen at all: only when there is something to do.</summary>
    public bool Appears => ShowEntry;

    public static CredentialFieldState Decide(bool isSet, CredentialHealth health)
    {
        // A failure always reveals the entry so it can be corrected, and reads as a warning.
        if (health.Status == CredentialStatus.Failing)
        {
            var detail = string.IsNullOrWhiteSpace(health.Message)
                ? "Last check failed — enter a new value."
                : $"Last check failed: {health.Message}";
            return new CredentialFieldState(ShowEntry: true, detail, IsWarning: true);
        }

        if (!isSet)
            return new CredentialFieldState(ShowEntry: true, "Not set.", IsWarning: false);

        // Set, and working or not yet exercised: nothing to do, so nothing to show.
        return new CredentialFieldState(ShowEntry: false, string.Empty, IsWarning: false);
    }
}
