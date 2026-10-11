using Pulp.Domain.Calendar;
using Pulp.Infrastructure.Calendar;

namespace Pulp.Tests.Infrastructure.Calendar;

/// <summary>What the nightly worker does with each way a night can fail, decided apart from WorkManager.</summary>
public class CalendarFailuresTests
{
    private static CalendarException Calendar(CalendarFailure failure, int? status = null) => new(failure, status, "invented");

    [Theory]
    [InlineData(CalendarFailure.Unavailable, CalendarAdvice.Retry)]
    [InlineData(CalendarFailure.Throttled, CalendarAdvice.Retry)]
    [InlineData(CalendarFailure.Rejected, CalendarAdvice.GiveUp)]
    [InlineData(CalendarFailure.Malformed, CalendarAdvice.GiveUp)]
    public void ACalendarFailureIsRetriedOnlyWhenTonightsTryMayMendIt(CalendarFailure failure, CalendarAdvice expected)
    {
        Assert.Equal(expected, CalendarFailures.Advise(Calendar(failure)));
    }

    [Fact]
    public void NotBeingSignedInIsAQuietRetry()
    {
        Assert.Equal(CalendarAdvice.QuietRetry, CalendarFailures.Advise(new InvalidOperationException("Sign in with Google first.")));
    }

    [Fact]
    public void AnotherInvalidOperationIsAProgrammingErrorNotAQuietRetry()
    {
        Assert.Equal(CalendarAdvice.GiveUp, CalendarFailures.Advise(new InvalidOperationException("Something else broke.")));
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(InvalidCastException))]
    public void AProgrammingErrorIsNotRetried(Type type)
    {
        Assert.Equal(CalendarAdvice.GiveUp, CalendarFailures.Advise((Exception)Activator.CreateInstance(type)!));
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(HttpRequestException))]
    public void ATransientSystemErrorIsRetried(Type type)
    {
        Assert.Equal(CalendarAdvice.Retry, CalendarFailures.Advise((Exception)Activator.CreateInstance(type)!));
    }

    [Fact]
    public void AStoppedWorkerIsAQuietRetry()
    {
        Assert.Equal(CalendarAdvice.QuietRetry, CalendarFailures.Advise(new OperationCanceledException()));
    }

    [Fact]
    public void AnAggregateRetriesWhenAnyPartMayMendAndGivesUpWhenNoneCan()
    {
        var mixed = new CalendarSyncException([Calendar(CalendarFailure.Rejected), Calendar(CalendarFailure.Unavailable)], new CalendarSyncResult(0, 0, 0, 0));
        var hopeless = new CalendarSyncException([Calendar(CalendarFailure.Malformed)], new CalendarSyncResult(0, 0, 0, 0));

        Assert.Equal(CalendarAdvice.Retry, CalendarFailures.Advise(mixed));
        Assert.Equal(CalendarAdvice.GiveUp, CalendarFailures.Advise(hopeless));
    }

    [Fact]
    public void FlatteningHandsBackEachInnerFailureOnce()
    {
        var first = Calendar(CalendarFailure.Rejected, 401);
        var second = Calendar(CalendarFailure.Unavailable);
        var aggregate = new CalendarSyncException([first, second], new CalendarSyncResult(0, 0, 0, 0));

        Assert.Equal([first, second], CalendarFailures.Parts(aggregate));
        Assert.Equal([first], CalendarFailures.Parts(first));
    }

    [Fact]
    public void ConsentIsWithdrawnByAnAggregateHoldingA401()
    {
        var aggregate = new CalendarSyncException([Calendar(CalendarFailure.Rejected, 401)], new CalendarSyncResult(0, 0, 0, 0));
        Assert.True(CalendarFailures.ConsentWithdrawn(aggregate));
        Assert.False(CalendarFailures.ConsentWithdrawn(new InvalidOperationException("x")));
    }
}
