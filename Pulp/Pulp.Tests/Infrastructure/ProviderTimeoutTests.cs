using Pulp.Domain.Catalogue;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Tests.Infrastructure;

/// <summary>
/// A source that never answers. The shared <see cref="HttpClient"/> gives up after its
/// <see cref="HttpClient.Timeout"/> and throws a <see cref="TaskCanceledException"/>, which is an
/// <see cref="OperationCanceledException"/> rather than an <see cref="HttpRequestException"/> — so a
/// timeout has to be read as the source being unavailable, while the caller's own cancellation must
/// still propagate as a cancellation.
/// </summary>
public class ProviderTimeoutTests
{
    [Fact]
    public async Task ATimedOutSourceIsUnavailableNotACancellation()
    {
        var client = new TvMazeClient(new HttpClient(new SilentHandler()) { Timeout = TimeSpan.FromMilliseconds(50) }, new LookupTally());

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => client.GetAirDatesByImdbAsync("tt9900001", CancellationToken.None));

        Assert.Equal(LookupFailure.Unavailable, thrown.Failure);
        Assert.Equal(LookupSource.TvMaze, thrown.Source);
        Assert.Null(thrown.StatusCode);
    }

    [Fact]
    public async Task TheCallersOwnCancellationStillPropagates()
    {
        var client = new TvMazeClient(new HttpClient(new SilentHandler()) { Timeout = TimeSpan.FromMinutes(5) }, new LookupTally());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAirDatesByImdbAsync("tt9900001", cancellation.Token));
    }

    /// <summary>Never answers; ends only when the request is cancelled, by the caller or by the client's timeout.</summary>
    private sealed class SilentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The silent handler returned without being cancelled.");
        }
    }
}
