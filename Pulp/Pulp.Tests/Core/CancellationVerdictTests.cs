using Pulp.Domain.Core;

namespace Pulp.Tests.Core;

/// <summary>
/// A Drive or HTTP timeout is a <see cref="TaskCanceledException"/>, which is an
/// <see cref="OperationCanceledException"/>, so "not a cancellation" is the wrong test for "a real failure".
/// Only a cancellation the caller asked for, on the token it passed, is a user cancel.
/// </summary>
public class CancellationVerdictTests
{
    [Fact]
    public void ATimeoutWithATokenNobodyCancelledIsNotAUserCancel() =>
        Assert.False(CancellationVerdict.IsUserCancel(new TaskCanceledException("timed out"), CancellationToken.None));

    [Fact]
    public void ATimeoutWhileTheTokenIsNotCancelledIsNotAUserCancel()
    {
        using var source = new CancellationTokenSource();

        Assert.False(CancellationVerdict.IsUserCancel(new TaskCanceledException("timed out"), source.Token));
    }

    [Fact]
    public void ACancellationOnATokenThatWasCancelledIsAUserCancel()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.True(CancellationVerdict.IsUserCancel(new OperationCanceledException(source.Token), source.Token));
        Assert.True(CancellationVerdict.IsUserCancel(new TaskCanceledException("gone"), source.Token));
    }

    [Fact]
    public void AnythingThatIsNotACancellationIsNeverAUserCancelEvenWithACancelledToken()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.False(CancellationVerdict.IsUserCancel(new IOException("disk"), source.Token));
    }
}
