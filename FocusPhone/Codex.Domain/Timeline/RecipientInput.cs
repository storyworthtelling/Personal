namespace Codex.Domain.Timeline;

/// <summary>
/// Validates manually entered recipients for the new-conversation flow before
/// they are handed to a dial/text intent or the email composer.
/// </summary>
public static class RecipientInput
{
    public static bool TryCreatePhone(string raw, out string dialable)
    {
        var trimmed = raw.Trim();
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        dialable = trimmed.StartsWith('+') ? $"+{digits}" : digits;
        return digits.Length > 0;
    }

    public static bool TryCreateEmail(string raw, out string address)
    {
        address = raw.Trim().ToLowerInvariant();
        var at = address.IndexOf('@');
        var domain = at >= 0 ? address[(at + 1)..] : "";
        return at > 0
            && !address.Contains(' ')
            && address.IndexOf('@', at + 1) < 0
            && domain.Contains('.')
            && !domain.StartsWith('.')
            && !domain.EndsWith('.');
    }
}
