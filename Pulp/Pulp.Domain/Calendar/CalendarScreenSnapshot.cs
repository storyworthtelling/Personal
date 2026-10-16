using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Calendar;

/// <summary>
/// Assembles what the nightly writer reads (<see cref="CalendarFeedSnapshot"/>) from the head's reads, and
/// decides the one thing that is not a plain copy: the role. An owner who has not yet consented to the two
/// calendar scopes is handed <see cref="HouseholdFileRole.Unknown"/>, which <see cref="PulpCalendarAccess"/>
/// reads as "wait" — nothing is created or written and no request leaves the phone, because a token request
/// for a scope nobody consented to raises a system sign-in prompt in the middle of the night.
/// </summary>
public static class CalendarScreenSnapshot
{
    public static CalendarFeedSnapshot Build(
        CalendarSources sources,
        IReadOnlyDictionary<ViewerId, IReadOnlySet<LeagueId>> heldByAdult,
        Func<ViewerId, string> nameOf,
        HouseholdFileRole role,
        bool connected,
        string? calendarId,
        string? signedInAccount,
        TimeZoneInfo zone,
        IReadOnlyList<string>? otherAdultAccounts = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(heldByAdult);
        ArgumentNullException.ThrowIfNull(nameOf);
        ArgumentNullException.ThrowIfNull(zone);

        var account = string.IsNullOrWhiteSpace(signedInAccount) ? null : signedInAccount.Trim();
        var writes = role == HouseholdFileRole.Owner && connected && account is not null;
        var effective = role == HouseholdFileRole.Owner && !writes ? HouseholdFileRole.Unknown : role;

        // The other adult's address comes from the household file (D304); the owner is first, and only a phone
        // that writes shares anything. CalendarSharing.Missing drops the owner's own address and duplicates.
        return new CalendarFeedSnapshot(
            sources,
            heldByAdult,
            nameOf,
            effective,
            calendarId,
            writes ? account! : string.Empty,
            writes ? Shared(account!, otherAdultAccounts) : [],
            zone);
    }

    private static List<string> Shared(string owner, IReadOnlyList<string>? others) =>
        CalendarSharing.Missing(owner, others ?? [], []).Prepend(owner).ToList();
}

/// <summary>Whether the account picked for the calendar may be used.</summary>
public enum CalendarAccountVerdict
{
    Accept,
    Refuse,
}

/// <summary>What Settings and the Vault page say about the calendar, and whether they offer to connect it.</summary>
public static class CalendarScreenConnection
{
    public const string NotConnected = "Calendar not connected yet";

    public static string Sentence(HouseholdFileRole role, bool connected, IReadOnlyList<string>? unshared = null) => role switch
    {
        HouseholdFileRole.Owner when !connected => NotConnected,
        HouseholdFileRole.Owner when unshared is { Count: > 0 } =>
            "Calendar connected. Pulp writes the household calendar each night. " + ShareProblem(unshared),
        HouseholdFileRole.Owner => "Calendar connected. Pulp writes the household calendar each night.",
        HouseholdFileRole.SharedWith => "The household calendar is written from the owner's phone.",
        _ => "The calendar waits until this phone knows whose it is.",
    };

    /// <summary>D359: the plain sentence for addresses Google refused to share the calendar with.</summary>
    public static string ShareProblem(IReadOnlyList<string> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        return "Couldn't share with " + string.Join(" or ", accounts) + ".";
    }

    /// <summary>Only the owner's phone writes, so only it needs the consent (D145).</summary>
    public static bool OffersConnect(HouseholdFileRole role, bool connected) =>
        role == HouseholdFileRole.Owner && !connected;

    /// <summary>
    /// The calendar's sign-in shares one stored Google account with Drive (the vault and the voice profile), so
    /// a different pick would repoint all of them. The first pick, or the same account again, is accepted.
    /// </summary>
    public static CalendarAccountVerdict JudgeAccount(string storedAccount, string chosenAccount) =>
        string.IsNullOrWhiteSpace(storedAccount) || string.Equals(storedAccount.Trim(), chosenAccount?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? CalendarAccountVerdict.Accept
            : CalendarAccountVerdict.Refuse;

    /// <summary>The sentence for the Vault page when the picked account is not the one Pulp already uses.</summary>
    public static string DifferentAccount(string storedAccount) =>
        $"Pulp already uses {storedAccount} for Drive. Pick that account to connect the calendar.";
}
