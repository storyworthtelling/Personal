namespace FocusPhone.Infrastructure.Email;

/// <summary>
/// Normalises the Gmail address and app password before they reach IMAP/SMTP.
/// Google presents the 16-character app password as four space-separated
/// groups ("abcd efgh ijkl mnop"); those spaces are for reading only and Gmail
/// rejects them with "Application-specific password required". Pasting the
/// address can likewise carry stray spaces or mixed case. Both are cleaned in
/// one place so the settings screen, the account store, and the client agree.
/// </summary>
public static class GmailCredentials
{
    /// <summary>Removes every whitespace character from a pasted app password.</summary>
    public static string NormalizeAppPassword(string? appPassword)
    {
        if (string.IsNullOrEmpty(appPassword))
        {
            return string.Empty;
        }

        return string.Concat(appPassword.Where(c => !char.IsWhiteSpace(c)));
    }

    /// <summary>Trims and lowercases a pasted Gmail address (Gmail is case-insensitive).</summary>
    public static string NormalizeAddress(string? address) =>
        address?.Trim().ToLowerInvariant() ?? string.Empty;
}
