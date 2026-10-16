using Pulp.Domain.Household;

namespace Pulp.Domain.Calendar;

/// <summary>What this install does about the Pulp calendar (D145).</summary>
public enum CalendarStand
{
    /// <summary>The owner has no calendar yet: make it, then write to it.</summary>
    CreateAndWrite,

    /// <summary>The owner's install, calendar known.</summary>
    Write,

    /// <summary>The other adult's install: it holds the id and reads; it writes nothing.</summary>
    Find,

    /// <summary>Nothing to do: not the owner, or not yet known to be.</summary>
    Wait,
}

public static class PulpCalendarAccess
{
    /// <summary>
    /// Only the owner's account creates or writes (the same rule as the household file,
    /// <see cref="HouseholdSync.MayCreateFile"/>); a phone that does not yet know its role waits, because
    /// guessing "owner" would make a second calendar in the other adult's account.
    /// </summary>
    public static CalendarStand Decide(HouseholdFileRole role, string? calendarId)
    {
        var known = !string.IsNullOrWhiteSpace(calendarId);
        return role switch
        {
            HouseholdFileRole.Owner => known ? CalendarStand.Write : CalendarStand.CreateAndWrite,
            HouseholdFileRole.SharedWith when known => CalendarStand.Find,
            _ => CalendarStand.Wait,
        };
    }
}

public static class CalendarSharing
{
    /// <summary>
    /// The other adult reads and does not edit: Pulp is the only writer, and a hand edit would be undone
    /// by the next nightly pass. Reading is all Foreword needs from it.
    /// </summary>
    public const string Role = "reader";

    /// <summary>The adults the calendar still has to be shared with: not the owner, not already shared.</summary>
    public static IReadOnlyList<string> Missing(string owner, IEnumerable<string> adults, IEnumerable<string> alreadyShared)
    {
        var skip = new HashSet<string>(alreadyShared.Append(owner), StringComparer.OrdinalIgnoreCase);
        return adults
            .Where(account => !string.IsNullOrWhiteSpace(account))
            .Select(account => account.Trim())
            .Where(account => !skip.Contains(account))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>One address Google would not share the calendar with, and why; the night's writing carries on without it (D359).</summary>
public sealed record CalendarShareFailure(string Account, Exception Failure);

/// <summary>
/// The Pulp calendar's access, in order: create (owner only), save the id to the household at once
/// (before sharing, so a failure there cannot orphan a second calendar on the next try), then share.
/// </summary>
public static class CalendarHousehold
{
    public const string CalendarName = "Pulp";

    public static async Task<string?> EnsureAsync(
        IPulpCalendar calendar,
        HouseholdFileRole role,
        string? knownId,
        string ownerAccount,
        IReadOnlyList<string> adultAccounts,
        Func<string, Task> saveId,
        TimeZoneInfo zone,
        CancellationToken cancellationToken,
        ICollection<CalendarShareFailure>? shareFailures = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(saveId);

        var stand = PulpCalendarAccess.Decide(role, knownId);
        switch (stand)
        {
            case CalendarStand.Wait:
                return null;
            case CalendarStand.Find:
                return knownId;
        }

        var id = knownId;
        if (stand == CalendarStand.CreateAndWrite)
        {
            id = await calendar.CreateAsync(CalendarName, zone, cancellationToken);
            await saveId(id);
        }

        var shared = await calendar.SharedWithAsync(id!, cancellationToken);
        foreach (var account in CalendarSharing.Missing(ownerAccount, adultAccounts, shared))
        {
            try
            {
                await calendar.ShareAsReaderAsync(id!, account, cancellationToken);
            }
            catch (Exception exception) when (shareFailures is not null && !cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
            {
                // One bad address (a typo in the household file) must not stop the night's events (D359); the
                // next night asks again, since Missing() is computed from who the calendar is actually shared with.
                shareFailures.Add(new CalendarShareFailure(account, exception));
            }
        }

        return id;
    }
}
