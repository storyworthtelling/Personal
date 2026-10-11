using Pulp.Domain.Calendar;

namespace Pulp.Tests.Calendar;

/// <summary>
/// Source-level guards for the calendar writer. Each asserts it found the code it checks, so an
/// empty match fails instead of passing.
/// </summary>
public class CalendarWriteGuardTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(PulpSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static IReadOnlyList<string> Sources() =>
        PulpSource.Files(".cs").Where(file => !file.Contains("Pulp.Tests", StringComparison.Ordinal)).ToList();

    /// <summary>Only the owner-gated household step may create the calendar.</summary>
    [Fact]
    public void OnlyCalendarHouseholdCreatesTheCalendar()
    {
        var creators = Sources()
            .Where(file => File.ReadAllText(file).Contains("calendar.CreateAsync(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["CalendarHousehold.cs"], creators);
        var household = Read("Pulp.Domain/Calendar/CalendarHousehold.cs");
        Assert.Contains("PulpCalendarAccess.Decide(role, knownId)", household);
        Assert.Contains("CalendarStand.Wait", household);
    }

    [Fact]
    public void TheWorkerNeverCreatesTheCalendarItself()
    {
        var worker = Read("Pulp.Android/Calendar/CalendarSyncWorker.cs");
        Assert.DoesNotContain("CreateAsync(", worker);
        Assert.Contains("CalendarNightly.RunAsync", worker);
        Assert.Contains("CalendarHousehold.EnsureAsync", Read("Pulp.Domain/Calendar/CalendarNightly.cs"));
    }

    /// <summary>Every event write in the client builds its URL through the one silent-update helper.</summary>
    [Fact]
    public void EveryEventWriteIsSilent()
    {
        var client = Read("Pulp.Infrastructure/Calendar/GoogleCalendarClient.cs");
        var writes = new[] { "HttpMethod.Post, Silent(EventsUrl(", "HttpMethod.Patch, Silent(", "HttpMethod.Delete, Silent(" };
        foreach (var write in writes)
        {
            Assert.Contains(write, client);
        }

        Assert.Contains("sendUpdates=none", client);
        Assert.DoesNotContain("sendUpdates=all", client);
        Assert.DoesNotContain("sendUpdates=externalOnly", client);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(client, @"Silent\(").Count - 1);
    }

    /// <summary>The writer reaches an event only through a plan operation, which only keyed events produce.</summary>
    [Fact]
    public void TheWriterOnlyActsOnPlanOperations()
    {
        var writer = Read("Pulp.Domain/Calendar/CalendarWriter.cs");
        foreach (var call in new[] { "calendar.PatchAsync(calendarId, move.EventId", "calendar.DeleteAsync(calendarId, delete.EventId", "calendar.InsertAsync(calendarId, create.Draft" })
        {
            Assert.Contains(call, writer);
        }

        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(writer, @"calendar\.(Patch|Delete|Insert)Async\(").Count);
        var plan = Read("Pulp.Domain/Calendar/CalendarSyncPlan.cs");
        Assert.Contains("string.IsNullOrWhiteSpace(item.Key)", plan);
    }

    [Fact]
    public void TheClientReadsAndWritesTheKeyUnderOneName()
    {
        var client = Read("Pulp.Infrastructure/Calendar/GoogleCalendarClient.cs");
        Assert.Contains("PulpEventKey.Property", client);
        Assert.Equal("pulpKey", PulpEventKey.Property);
    }
}
