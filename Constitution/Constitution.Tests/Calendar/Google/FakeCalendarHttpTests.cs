using System.Net;
using Google;
using Google.Apis.Calendar.v3.Data;

namespace Constitution.Tests.Calendar.Google;

/// <summary>The fake under the Google client is itself checked, so the adapters' tests stand on something that shows what was sent.</summary>
public class FakeCalendarHttpTests
{
    [Fact]
    public async Task TheClientMakesTheRealRequestAndReadsTheRealJson()
    {
        var http = new FakeCalendarHttp
        {
            Reply = _ => FakeCalendarHttp.Json("{\"items\":[{\"id\":\"e1\",\"summary\":\"Dentist\",\"start\":{\"dateTime\":\"2026-11-03T09:00:00-05:00\"},\"end\":{\"dateTime\":\"2026-11-03T10:00:00-05:00\"}}]}"),
        };
        var service = http.Service();

        var request = service.Events.List("sam@example.com");
        request.SingleEvents = true;
        var events = await request.ExecuteAsync();

        var sent = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("/calendar/v3/calendars/sam@example.com/events", Uri.UnescapeDataString(sent.Path));
        Assert.Contains("singleEvents=True", sent.Query, StringComparison.OrdinalIgnoreCase);
        var only = Assert.Single(events.Items);
        Assert.Equal("Dentist", only.Summary);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(-5)), only.Start.DateTimeDateTimeOffset);
    }

    [Fact]
    public async Task AnErrorReplyIsAGoogleApiExceptionWithItsStatus()
    {
        var http = new FakeCalendarHttp { Reply = _ => FakeCalendarHttp.Error(HttpStatusCode.Forbidden) };

        var failure = await Assert.ThrowsAsync<GoogleApiException>(() => http.Service().Events.List("x@example.com").ExecuteAsync());

        Assert.Equal(HttpStatusCode.Forbidden, failure.HttpStatusCode);
    }

    [Fact]
    public async Task ABodySentIsRecorded()
    {
        var http = new FakeCalendarHttp { Reply = _ => FakeCalendarHttp.Json("{\"id\":\"made\"}") };

        var made = await http.Service().Events.Insert(new Event { Summary = "Workout · 40 min" }, "x@example.com").ExecuteAsync();

        Assert.Equal("made", made.Id);
        Assert.Contains("Workout", Assert.Single(http.Requests).Body, StringComparison.Ordinal);
    }
}
