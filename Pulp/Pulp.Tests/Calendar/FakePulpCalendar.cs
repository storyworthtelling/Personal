using Pulp.Domain.Calendar;

namespace Pulp.Tests.Calendar;

/// <summary>An in-memory Pulp calendar that records what was done to it, with invented events.</summary>
public sealed class FakePulpCalendar : IPulpCalendar
{
    public int Creates { get; private set; }

    public List<string> Shared { get; } = [];

    public List<string> AlreadyShared { get; } = [];

    public List<ExistingEvent> Held { get; } = [];

    public List<string> Calls { get; } = [];

    public Action? OnCreate { get; set; }

    public Action? OnShare { get; set; }

    public Task<string> CreateAsync(string title, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        Creates++;
        OnCreate?.Invoke();
        return Task.FromResult("cal-new");
    }

    public Task<IReadOnlyList<string>> SharedWithAsync(string calendarId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(AlreadyShared);

    public Task ShareAsReaderAsync(string calendarId, string account, CancellationToken cancellationToken)
    {
        OnShare?.Invoke();
        Shared.Add(account);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExistingEvent>> ListAsync(string calendarId, DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExistingEvent>>(Held.ToList());

    public Task InsertAsync(string calendarId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        Calls.Add("insert:" + draft.Key);
        return Task.CompletedTask;
    }

    public Task PatchAsync(string calendarId, string eventId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        Calls.Add("patch:" + eventId);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string calendarId, string eventId, CancellationToken cancellationToken)
    {
        Calls.Add("delete:" + eventId);
        return Task.CompletedTask;
    }
}
