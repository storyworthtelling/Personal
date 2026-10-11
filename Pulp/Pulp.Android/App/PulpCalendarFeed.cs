using Codex.Ui.Voice;
using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Sports;

namespace Pulp.App;

/// <summary>
/// What the nightly writer reads (<see cref="ICalendarFeed"/>), composed once in <see cref="PulpServices"/>.
/// Only the owner's connected phone writes (D145), so every other phone is handed a snapshot with no sources
/// and no network is touched for it: it still carries the household's calendar id, which is all the other
/// adult's phone needs. The owner's id is saved into the household file (<c>HouseholdMaps.PulpCalendar</c>),
/// so the other phone and Foreword find the same calendar rather than a second one being made.
/// </summary>
internal sealed class PulpCalendarFeed(PulpServices services) : ICalendarFeed
{
    public async Task<CalendarFeedSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var role = services.HouseholdFileRole;
        var connected = services.CalendarConnection.Connected;
        var account = VoiceGoogleAccount.Email;

        if (role != HouseholdFileRole.Owner || !connected)
        {
            var household = await services.HouseholdAsync(cancellationToken);
            return CalendarScreenSnapshot.Build(
                new CalendarSources([], [], [], []),
                new Dictionary<ViewerId, IReadOnlySet<LeagueId>>(),
                household.NameOf,
                role,
                connected,
                household.PulpCalendarId,
                account,
                TimeZoneInfo.Local);
        }

        var read = await new CalendarReads(services).ReadAsync(cancellationToken);
        return CalendarScreenSnapshot.Build(
            read.Sources, read.HeldByAdult, read.Household.NameOf, role, connected, read.Household.PulpCalendarId, account, read.Zone);
    }

    public Task SaveCalendarIdAsync(string calendarId, CancellationToken cancellationToken) =>
        services.PutAsync(HouseholdMaps.PulpCalendar, new PulpCalendarEntry(calendarId), cancellationToken);
}
