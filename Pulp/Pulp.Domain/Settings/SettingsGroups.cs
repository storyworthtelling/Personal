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
    // The suite's shared names (Storyworthtelling.Updates.SettingsAnatomy), spelled here because Domain stays free of it.
    public const string Vault = "Vault";
    public const string About = "About";
    public const string Diagnostics = "Diagnostics";

    public const string DiagnosticsMeta = "Export the log";

    /// <summary>The suite's order (audit W8.4b): the Vault first, Pulp's own groups, Diagnostics last.</summary>
    public static IReadOnlyList<string> Order { get; } = [Vault, Services, Household, Discover, About, Diagnostics];

    /// <summary>"Sam, Alex, Robin" — everyone on the roster, by the name they go by.</summary>
    public static string HouseholdMeta(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var listed = names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList();
        return listed.Count == 0 ? "Nobody yet" : string.Join(", ", listed);
    }

    /// <summary>How many keys want entering; nothing when none does, since a working key is not on the screen.</summary>
    public static string? VaultMeta(IEnumerable<bool> keysNeedingEntry)
    {
        ArgumentNullException.ThrowIfNull(keysNeedingEntry);
        var keys = keysNeedingEntry.ToList();
        if (keys.Count == 0)
        {
            throw new ArgumentException("The vault line is about Pulp’s keys; it was given none to ask about.", nameof(keysNeedingEntry));
        }

        var needed = keys.Count(needs => needs);
        return needed switch
        {
            0 => null,
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

    /// <summary>
    /// The sports tables as About prints them (D140): each league and the feed it is read from (D208), then the
    /// broadcasters a game's Play opens in the household's own services (D310).
    /// </summary>
    public static IReadOnlyList<string> SportsTables() =>
    [
        .. Pulp.Domain.Sports.SportsSourceTable.Default.AboutLines(),
        "Play opens a game in your own service when its channel is one of these:",
        .. Pulp.Domain.Services.BroadcasterTable.Public.AboutLines(),
    ];
}
