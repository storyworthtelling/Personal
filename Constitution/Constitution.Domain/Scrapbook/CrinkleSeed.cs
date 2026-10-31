using System.Text;

namespace Constitution.Domain.Scrapbook;

/// <summary>
/// A page's ground seed (C-3.2.2). The crinkle must be the same on every launch and every phone, and
/// <c>string.GetHashCode</c> is randomised per process, so the seed is FNV-1a over the area id's
/// UTF-8 bytes: a fixed function, and one seed per area so the four crinkled pages are not copies.
/// </summary>
public static class CrinkleSeed
{
    public static int For(string areaId)
    {
        ArgumentNullException.ThrowIfNull(areaId);

        var hash = 2166136261u;
        foreach (var value in Encoding.UTF8.GetBytes(areaId))
        {
            hash = (hash ^ value) * 16777619u;
        }

        return unchecked((int)hash);
    }
}
