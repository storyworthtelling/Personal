namespace Index.Domain.Household;

/// <summary>
/// What leaving a Settings field commits (task 2.7, mockup 10: "no pinned bar — every field saves
/// itself"). The page used to persist all three fields from one Save button; with the button gone each
/// field commits when it is left, so the page asks this which of them actually changed and does only that.
///
/// <para><b>An unchanged value commits nothing</b>: no write, no restore, no vault publish. A blur on a
/// field the owner only looked at must not re-publish the vault.</para>
///
/// <para><b>The Plaid pair commits as a pair, and never empty.</b> Replace clears both boxes so the new
/// values are typed fresh; leaving one of them before the other is typed would, if an empty value
/// committed, blank the credentials that were still working. So the pair stores only when both halves
/// have text and either differs from what is stored. The passphrase is the opposite: clearing it is a
/// real change (the owner stopping the vault), and it is compared untrimmed, as it was typed.</para>
/// </summary>
public sealed record SettingsCommitPlan(bool StorePlaid, bool StorePassphrase)
{
    public bool Any => StorePlaid || StorePassphrase;

    public static SettingsCommitPlan Decide(
        string storedClientId, string storedSecret, string storedPassphrase,
        string? typedClientId, string? typedSecret, string? typedPassphrase)
    {
        var clientId = (typedClientId ?? string.Empty).Trim();
        var secret = (typedSecret ?? string.Empty).Trim();
        var passphrase = typedPassphrase ?? string.Empty;

        var pairComplete = clientId.Length > 0 && secret.Length > 0;
        var pairChanged = !string.Equals(clientId, storedClientId, StringComparison.Ordinal)
            || !string.Equals(secret, storedSecret, StringComparison.Ordinal);

        return new SettingsCommitPlan(
            StorePlaid: pairComplete && pairChanged,
            StorePassphrase: !string.Equals(passphrase, storedPassphrase ?? string.Empty, StringComparison.Ordinal));
    }
}
