using System.Globalization;
using System.Text;
using Pulp.Domain.Core;

namespace Pulp.Domain.People;

/// <summary>
/// A person's name and the id they are written under — the one place first run and the Household page
/// both ask, so the two cannot disagree about an id. The viewer record holds the id and the child flag
/// only (the name is its own record, <see cref="Household.Records.ViewerNameEntry"/>), so the id is the name made into one word:
/// lowercased, each run of anything that is not a letter or digit become one hyphen. That makes it
/// stable (the same name, the same id) and readable back as the name (<see cref="Display"/>). It is
/// collision-safe within the roster: an id already taken is never handed out again.
/// </summary>
public static class ViewerNaming
{
    public const string NameNeeded = "Write the person's name.";

    public const string NeedsALetter = "A name needs at least one letter or number.";

    public static string AlreadyThere(string name) =>
        $"{name.Trim()} is already in the household. Add an initial to tell them apart.";

    /// <summary>The name as typed, trimmed with its inner runs of space made single; null when nothing is left.</summary>
    public static string? Clean(string? typed) =>
        string.IsNullOrWhiteSpace(typed) ? null : string.Join(' ', typed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The problem with <paramref name="name"/>, in one plain sentence, or null when it will do.</summary>
    public static string? Check(string? name, IEnumerable<ViewerId> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        if (string.IsNullOrWhiteSpace(name))
        {
            return NameNeeded;
        }

        var slug = Slug(name);
        if (slug.Length == 0)
        {
            return NeedsALetter;
        }

        return taken.Contains(new ViewerId(slug)) ? AlreadyThere(Display(new ViewerId(slug))) : null;
    }

    /// <summary>The id for <paramref name="name"/>, numbered past any id in <paramref name="taken"/>; refused when it has no letter or number.</summary>
    public static ViewerId Mint(string name, IEnumerable<ViewerId> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        var slug = Slug(name ?? string.Empty);
        if (slug.Length == 0)
        {
            throw new ArgumentException($"'{name}' has no letter or number to make an id from.", nameof(name));
        }

        var used = taken.ToHashSet();
        var id = new ViewerId(slug);
        for (var suffix = 2; used.Contains(id); suffix++)
        {
            id = new ViewerId($"{slug}-{suffix}");
        }

        return id;
    }

    /// <summary>The id read back as a name: hyphens as spaces, each word capitalised.</summary>
    public static string Display(ViewerId id)
    {
        var words = id.Value.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..]);
        return string.Join(' ', words);
    }

    private static string Slug(string name)
    {
        var builder = new StringBuilder();
        var pendingHyphen = false;
        foreach (var character in name.Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return builder.ToString();
    }
}
