using System.Net;
using Constitution.Infrastructure.Calendar;
using Google;

namespace Constitution.Tests.Calendar.Google;

/// <summary>
/// The failures Google Calendar can be expected to give, in plain sentences with no number, address or calendar content in them: they go to a
/// notice's reason and a trail. Anything else is a bug and is not described here, so it propagates with its stack.
/// </summary>
public class CalendarErrorsTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Google did not accept the sign-in for a calendar.")]
    [InlineData(HttpStatusCode.Forbidden, "Google refused access to a calendar; it may not be shared.")]
    [InlineData(HttpStatusCode.NotFound, "A calendar was not found; it may not be shared.")]
    [InlineData(HttpStatusCode.Gone, "A calendar or event is gone.")]
    [InlineData(HttpStatusCode.TooManyRequests, "Google asked Constitution to slow down.")]
    [InlineData(HttpStatusCode.InternalServerError, "Google could not answer.")]
    [InlineData(HttpStatusCode.BadGateway, "Google could not answer.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Google could not answer.")]
    [InlineData(HttpStatusCode.BadRequest, "Google answered with an error.")]
    public void EachStatusHasItsSentence(HttpStatusCode status, string expected)
    {
        Assert.Equal(expected, CalendarErrors.Reason(status));
    }

    [Fact]
    public void NoSentenceHoldsANumber()
    {
        foreach (var status in Enum.GetValues<HttpStatusCode>())
        {
            Assert.DoesNotContain(CalendarErrors.Reason(status), character => char.IsDigit(character));
        }
    }

    [Fact]
    public void AGoogleErrorIsDescribedByItsStatus()
    {
        var failure = new GoogleApiException("Calendar", "The text Google gave, which may name a calendar.") { HttpStatusCode = HttpStatusCode.Forbidden };

        Assert.True(CalendarErrors.TryDescribe(failure, CancellationToken.None, out var reason));
        Assert.Equal("Google refused access to a calendar; it may not be shared.", reason);
        Assert.DoesNotContain("name a calendar", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANetworkFailureIsDescribed()
    {
        Assert.True(CalendarErrors.TryDescribe(new HttpRequestException("The text the platform gave."), CancellationToken.None, out var reason));
        Assert.Equal("A calendar could not be reached.", reason);
    }

    [Fact]
    public void ATimeoutIsDescribed()
    {
        Assert.True(CalendarErrors.TryDescribe(new TaskCanceledException("Timed out.", new TimeoutException()), CancellationToken.None, out var reason));
        Assert.Equal("A calendar did not answer in time.", reason);
    }

    [Fact]
    public void ACancellationTheCallerAskedForIsNotAFailure()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.False(CalendarErrors.TryDescribe(new TaskCanceledException("Cancelled."), source.Token, out _));
        Assert.False(CalendarErrors.TryDescribe(new OperationCanceledException(source.Token), source.Token, out _));
    }

    [Fact]
    public void ABugIsNotDescribed()
    {
        Assert.False(CalendarErrors.TryDescribe(new InvalidOperationException("A bug."), CancellationToken.None, out _));
        Assert.False(CalendarErrors.TryDescribe(new NullReferenceException(), CancellationToken.None, out _));
    }

    [Fact]
    public void ASignInProblemKeepsItsOwnSentence()
    {
        var failure = new CalendarAccessException("Google sign-in is needed to read calendars.");

        Assert.True(CalendarErrors.TryDescribe(failure, CancellationToken.None, out var reason));
        Assert.Equal("Google sign-in is needed to read calendars.", reason);
    }
}

/// <summary>
/// Audit 11 §19: Google answers a rate limit or an exhausted quota with 403 too, and every 403 was told as "refused
/// access; it may not be shared" — the wrong thing to go and fix. The reason code tells them apart, as Pulp reads it.
/// </summary>
public class CalendarForbiddenReasonTests
{
    private static GoogleApiException Forbidden(string reason) =>
        new("Calendar", "The text Google gave.")
        {
            HttpStatusCode = HttpStatusCode.Forbidden,
            Error = new global::Google.Apis.Requests.RequestError
            {
                Code = 403,
                Errors = [new global::Google.Apis.Requests.SingleError { Reason = reason }],
            },
        };

    [Theory]
    [InlineData("rateLimitExceeded")]
    [InlineData("userRateLimitExceeded")]
    [InlineData("quotaExceeded")]
    [InlineData("dailyLimitExceeded")]
    [InlineData("calendarUsageLimitsExceeded")]
    public void ALimitIsToldAsALimit(string reason)
    {
        Assert.True(CalendarErrors.TryDescribe(Forbidden(reason), CancellationToken.None, out var sentence));
        Assert.Equal("Google asked Constitution to slow down.", sentence);
    }

    [Fact]
    public void ARealRefusalIsStillARefusal()
    {
        Assert.True(CalendarErrors.TryDescribe(Forbidden("forbidden"), CancellationToken.None, out var sentence));
        Assert.Equal("Google refused access to a calendar; it may not be shared.", sentence);
    }
}
