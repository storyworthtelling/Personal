using System.Security.Cryptography;

namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>
/// What a page says, as a SHA-256 of its bytes in lower-case hex. A render takes no clock and no device profile and draws
/// the same bytes twice (<c>PipelineDeterminismTests</c> holds it to that), so two pages have one fingerprint exactly when
/// they print the same. A fingerprint holds nothing of the page and can be stored and logged-by-count without leaking it.
/// </summary>
public static class DocumentFingerprint
{
    public static string Of(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        return Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();
    }
}
