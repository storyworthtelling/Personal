namespace Constitution.Domain.Credentials;

/// <summary>
/// Stable identifiers for the credentials Constitution tracks. A client reports health under one of
/// these at its HTTP choke point and a settings screen reads it back, so both ends share the string
/// rather than each holding a copy. The vault passphrase is deliberately absent: it is never stored
/// in the vault and is always shown in the clear.
/// </summary>
public static class CredentialIds
{
    /// <summary>The bed's cloud login (SleepIQ).</summary>
    public const string SleepNumber = "sleep-number";

    /// <summary>The reMarkable device token. The same spelling as the shared
    /// <c>RemarkableCredentialId.Value</c>, which a test pins; Domain does not reference that library.</summary>
    public const string Remarkable = "remarkable";

    /// <summary>The shared Claude key.</summary>
    public const string Claude = "claude";

    /// <summary>The shared Gemini key.</summary>
    public const string Gemini = "gemini";
}
