using System.Globalization;
using System.Text;
using Pulp.Domain.Core;

namespace Pulp.Domain.Settings;

/// <summary>
/// Names typed for a viewer, and the id a new viewer is given (Household, D88). The id keys everything
/// the viewer will own — sittings, vetoes, playlists, a child's rules — so it is minted once, never
/// reused for a second person of the same name, and never changed by a rename (the name is its own
/// record, <see cref="Household.Records.ViewerNameEntry"/>).
/// </summary>
public static class ViewerNaming
{
    private const string Fallback = "viewer";

    /// <summary>The name as typed, trimmed with its inner runs of space made single; null when nothing is left.</summary>
    public static string? Clean(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        return string.Join(' ', typed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The name in lower-case words joined by hyphens, numbered past any id already taken.</summary>
    public static ViewerId Mint(string name, IEnumerable<ViewerId> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        var clean = Clean(name) ?? throw new ArgumentException("A new viewer needs a name.", nameof(name));
        var used = taken.Select(id => id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stem = Slug(clean);
        var candidate = stem;
        for (var number = 2; used.Contains(candidate); number++)
        {
            candidate = stem + "-" + number.ToString(CultureInfo.InvariantCulture);
        }

        return new ViewerId(candidate);
    }

    private static string Slug(string name)
    {
        var slug = new StringBuilder();
        foreach (var letter in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(letter))
            {
                slug.Append(letter);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var text = slug.ToString().Trim('-');
        return text.Length == 0 ? Fallback : text;
    }
}
