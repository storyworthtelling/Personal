namespace Constitution.Domain.Credentials;

/// <summary>
/// Where the moment a credential was last saved by hand is kept, by credential. Only the credentials
/// a person types or pairs here have one; the shared keys arrive from the vault.
/// </summary>
public static class CredentialSaves
{
    /// <summary>The preference key holding when this credential was last saved, or null when it has none.</summary>
    public static string? KeyFor(string credentialId) => credentialId switch
    {
        CredentialIds.SleepNumber => ConstitutionIdentity.SleepNumberSavedKey,
        CredentialIds.Remarkable => ConstitutionIdentity.RemarkableSavedKey,
        _ => null,
    };
}
