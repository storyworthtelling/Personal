namespace FocusPhone.Domain.Timeline;

/// <summary>
/// Canonicalizes phone numbers and email addresses so the same contact's
/// calls, texts, and emails land in one thread regardless of formatting.
/// </summary>
public static class ContactKeyNormalizer
{
    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        // National significant number: compare on the last ten digits so that
        // +1/leading-1 variants of the same number collapse to one key, while
        // short codes (fewer than ten digits) pass through untouched.
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
