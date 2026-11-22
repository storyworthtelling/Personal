namespace Watch.Domain.Keys;

/// <summary>
/// A Google access token the phone hands a watch app (plan §2.4, A61, P14 W-4.1.2). It is never logged and never written
/// as text: <see cref="ToString"/> is a redaction, because interpolation is the one hole a type cannot close (C# calls
/// <c>ToString</c> for you, with no cast and no warning; Foreword's prayer text is held by the same rule), so a token in a
/// log line, an exception or a record's own text prints as <c>[access token]</c>. Its value leaves only as an
/// Authorization header (<see cref="BearerHeader"/>), read by one HTTP seam (<c>TheWatchHoldsNoVaultTests</c>), and as the
/// codec's own write.
/// </summary>
public sealed class AccessToken
{
    /// <summary>Shorter is no token Google issues; this also keeps a word or a stray id from passing for one.</summary>
    public const int ShortestLength = 20;

    /// <summary>Google's access tokens run to a few hundred characters; this is the ceiling the wire takes.</summary>
    public const int LongestLength = 2048;

    private const string Redaction = "[access token]";

    private AccessToken(string value)
    {
        Value = value;
    }

    /// <summary>The token itself: the codec's to write, in this assembly only.</summary>
    internal string Value { get; }

    /// <summary>
    /// The token, when <paramref name="value"/> has a bearer token's shape (RFC 6750 §2.1: letters, digits and
    /// <c>- . _ ~ + /</c>, ending in any <c>=</c>), 20 to 2048 characters; else null. Nothing about the value is said.
    /// </summary>
    public static AccessToken? From(string? value) => IsToken(value) ? new AccessToken(value!) : null;

    /// <summary>The Authorization header's value for a call made with this token.</summary>
    public string BearerHeader() => "Bearer " + Value;

    public override string ToString() => Redaction;

    public override bool Equals(object? obj) => obj is AccessToken other && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    private static bool IsToken(string? value)
    {
        if (value is not { Length: >= ShortestLength and <= LongestLength })
        {
            return false;
        }

        var body = value.TrimEnd('=');
        return body.Length > 0 && body.All(IsTokenCharacter);
    }

    private static bool IsTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~' or '+' or '/';
}
