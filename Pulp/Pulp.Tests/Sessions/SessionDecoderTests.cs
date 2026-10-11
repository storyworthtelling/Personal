using Pulp.Domain.Sessions;

namespace Pulp.Tests.Sessions;

/// <summary>
/// The text Flyleaf relays a playback session as (finding 2.3, D30): exact JSON in one extra. Garbled,
/// partial or implausible text is ignored, never guessed at. Packages and titles here are invented.
/// </summary>
public class SessionDecoderTests
{
    private const string Whole =
        "{\"package\":\"com.example.streamer\",\"title\":\"Harbor Lights\",\"positionMs\":90000,\"durationMs\":2640000,\"state\":\"playing\"}";

    [Fact]
    public void AWholeMessageDecodes()
    {
        Assert.True(SessionDecoder.TryDecode(Whole, out var message));

        Assert.Equal("com.example.streamer", message.Package);
        Assert.Equal("Harbor Lights", message.Title);
        Assert.Equal(TimeSpan.FromSeconds(90), message.Position);
        Assert.Equal(TimeSpan.FromMinutes(44), message.Duration);
        Assert.Equal(SessionPlayState.Playing, message.State);
    }

    [Fact]
    public void TheTitleIsOptionalAndSoIsADuration()
    {
        const string text = "{\"package\":\"com.example.streamer\",\"positionMs\":5,\"durationMs\":0,\"state\":\"paused\"}";

        Assert.True(SessionDecoder.TryDecode(text, out var message));
        Assert.Null(message.Title);
        Assert.Equal(TimeSpan.Zero, message.Duration);
        Assert.Equal(SessionPlayState.Paused, message.State);
    }

    [Fact]
    public void StoppedDecodes()
    {
        var text = Whole.Replace("\"playing\"", "\"stopped\"");

        Assert.True(SessionDecoder.TryDecode(text, out var message));
        Assert.Equal(SessionPlayState.Stopped, message.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("garbage")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"package\":\"com.example.streamer\",\"positionMs\":5,\"durationMs\":0,\"state\":\"dancing\"}")]
    [InlineData("{\"package\":\"com.example.streamer\",\"positionMs\":-1,\"durationMs\":0,\"state\":\"playing\"}")]
    [InlineData("{\"package\":\"com.example.streamer\",\"positionMs\":1,\"durationMs\":-1,\"state\":\"playing\"}")]
    [InlineData("{\"package\":\"com.example.streamer\",\"positionMs\":\"1\",\"durationMs\":0,\"state\":\"playing\"}")]
    [InlineData("{\"package\":\"com.example.streamer\",\"durationMs\":0,\"state\":\"playing\"}")]
    [InlineData("{\"package\":\"nope\",\"positionMs\":1,\"durationMs\":0,\"state\":\"playing\"}")]
    [InlineData("{\"package\":7,\"positionMs\":1,\"durationMs\":0,\"state\":\"playing\"}")]
    public void GarbledOrImplausibleTextIsIgnored(string? text)
    {
        Assert.False(SessionDecoder.TryDecode(text, out _));
    }

    [Fact]
    public void AnAbsurdPositionIsIgnored()
    {
        const string text = "{\"package\":\"com.example.streamer\",\"positionMs\":9999999999999999,\"durationMs\":0,\"state\":\"playing\"}";

        Assert.False(SessionDecoder.TryDecode(text, out _));
    }
}
