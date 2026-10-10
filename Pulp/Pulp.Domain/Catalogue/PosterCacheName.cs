using System.Security.Cryptography;
using System.Text;

namespace Pulp.Domain.Catalogue;

/// <summary>
/// The one name a poster is kept under in the phone's cache folder: a hash of its address, so one
/// poster is one file whichever screen asked for it first. Https only — a poster is a download the
/// phone makes on its own, and an address that is not https is not fetched at all (null).
/// </summary>
public static class PosterCacheName
{
    public static string? For(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..32].ToLowerInvariant() + ".img";
    }
}
