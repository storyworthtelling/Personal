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

    /// <summary>Calls that throw instead of succeeding, named as they appear in <see cref="Calls"/> ("insert:key").</summary>
    public HashSet<string> Failing { get; } = [];

    /// <summary>Runs after every event write is recorded, before it succeeds or throws.</summary>
    public Action? OnCall { get; set; }

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
        return Record("insert:" + draft.Key);
    }

    public Task PatchAsync(string calendarId, string eventId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        return Record("patch:" + eventId);
    }

    public Task DeleteAsync(string calendarId, string eventId, CancellationToken cancellationToken)
    {
        return Record("delete:" + eventId);
    }

    private Task Record(string call)
    {
        Calls.Add(call);
        OnCall?.Invoke();
        return Failing.Contains(call)
            ? throw new InvalidOperationException("The invented calendar refused " + call + ".")
            : Task.CompletedTask;
    }
}
