using System.Text;
using System.Text.Json;

namespace DailyLoader.Infrastructure.Remarkable;

internal static class JwtPayload
{
    internal static (string sub, DateTimeOffset expiry) Parse(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 3)
            throw new FormatException($"Invalid JWT: expected 3 parts, got {parts.Length}");

        var base64 = parts[1].Replace('-', '+').Replace('_', '/');
        var padded = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

        using var doc = JsonDocument.Parse(json);
        var sub = doc.RootElement.GetProperty("sub").GetString()
            ?? throw new FormatException("JWT missing 'sub' claim");
        var exp = doc.RootElement.GetProperty("exp").GetInt64();

        return (sub, DateTimeOffset.FromUnixTimeSeconds(exp));
    }
}
