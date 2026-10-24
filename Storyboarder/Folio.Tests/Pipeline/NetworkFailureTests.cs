using System.Net.Sockets;
using Folio.Domain.Pipeline;
using Xunit;

namespace Folio.Tests.Pipeline;

public class NetworkFailureTests
{
    // This is what the phone logged when it had no connection: one failed request per chunk, all in the
    // same second, each naming the host it could not look up.
    [Fact]
    public void AnUnresolvableHost_OnAndroid_IsOffline()
    {
        var error = new HttpRequestException("Connection failure",
            new IOException("Unable to resolve host \"api.anthropic.com\": No address associated with hostname"));

        Assert.True(NetworkFailure.IsOffline(error));
    }

    [Theory]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.TryAgain)]
    [InlineData(SocketError.NetworkUnreachable)]
    [InlineData(SocketError.HostUnreachable)]
    [InlineData(SocketError.NetworkDown)]
    public void ASocketThatCouldNotReachAnything_IsOffline(SocketError code)
    {
        Assert.True(NetworkFailure.IsOffline(new HttpRequestException("failed", new SocketException((int)code))));
    }

    // A connection that dropped part-way is a different thing: the request was accepted and billed, and
    // the reply was lost. That one is a real failure and keeps counting.
    [Fact]
    public void AConnectionAbortedMidStream_IsNotOffline()
    {
        var error = new IOException("Software caused connection abort",
            new SocketException((int)SocketError.ConnectionAborted));

        Assert.False(NetworkFailure.IsOffline(error));
    }

    [Fact]
    public void AnOrdinaryError_IsNotOffline()
    {
        Assert.False(NetworkFailure.IsOffline(new InvalidOperationException("bad json")));
    }
}
