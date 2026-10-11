using System.Globalization;

namespace Pulp.Domain.Settings;

/// <summary>
/// Settings' groups (D162, D250, mockup 09), in the drawn order, and the quiet line each one's header
/// carries. TV waits for its own phase (D187); there is no Sports group (teams are added from the Sports
/// tab, D67).
/// </summary>
public static class SettingsGroups
{
    public const string Services = "Services";
    public const string Household = "Household";
    public const string Discover = "Discover";
    public const string Vault = "Vault";
    public const string About = "About";
    public const string Diagnostics = "Diagnostics";

    public const string DiagnosticsMeta = "Export the log";

    public static IReadOnlyList<string> Order { get; } = [Services, Household, Discover, Vault, About, Diagnostics];

    /// <summary>"Sam, Alex, Robin" — everyone on the roster, by the name they go by.</summary>
    public static string HouseholdMeta(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var listed = names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList();
        return listed.Count == 0 ? "Nobody yet" : string.Join(", ", listed);
    }

    /// <summary>"Working" when no key wants entering (credential health), else how many do.</summary>
    public static string VaultMeta(IEnumerable<bool> keysNeedingEntry)
    {
        ArgumentNullException.ThrowIfNull(keysNeedingEntry);
        var keys = keysNeedingEntry.ToList();
        if (keys.Count == 0)
        {
            throw new ArgumentException("The vault line is about Pulp's keys; it was given none to ask about.", nameof(keysNeedingEntry));
        }

        var needed = keys.Count(needs => needs);
        return needed switch
        {
            0 => "Working",
            1 => "1 key needed",
            _ => needed.ToString(CultureInfo.InvariantCulture) + " keys needed",
        };
    }

    /// <summary>"Pulp 1.0.4"; "Pulp" when the package manager would not say.</summary>
    public static string AboutMeta(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "Pulp" : "Pulp " + version.Trim();

    /// <summary>
    /// D140's quiet credits line, one per source whose terms ask to be named where its data is shown
    /// (finding §1). The league feeds join it when the Sports adapters land (Release E, D208).
    /// </summary>
    public static IReadOnlyList<string> Credits { get; } =
    [
        "Where to watch: Streaming Availability, by Movie of the Night.",
        "Episode air dates: TVmaze, used under CC BY-SA.",
        "Ratings: OMDb, the Open Movie Database.",
        "Trailers: YouTube.",
    ];
}
