using FocusPhone.Domain.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class MediaButtonGestureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(400);

    private static MediaButtonGesture Gesture() => new(Window);

    [Fact]
    public void SinglePress_ResolvesToPlayPause()
    {
        var g = Gesture();
        g.Press(T0);
        Assert.Null(g.Flush(T0.AddMilliseconds(200))); // still within window
        Assert.Equal(MediaButtonAction.PlayPause, g.Flush(T0.AddMilliseconds(500)));
    }

    [Fact]
    public void DoublePress_ResolvesToNext()
    {
        var g = Gesture();
        g.Press(T0);
        g.Press(T0.AddMilliseconds(150));
        Assert.Equal(MediaButtonAction.Next, g.Flush(T0.AddMilliseconds(600)));
    }

    [Fact]
    public void TriplePress_ResolvesToPrevious()
    {
        var g = Gesture();
        g.Press(T0);
        g.Press(T0.AddMilliseconds(120));
        g.Press(T0.AddMilliseconds(240));
        Assert.Equal(MediaButtonAction.Previous, g.Flush(T0.AddMilliseconds(700)));
    }

    [Fact]
    public void PressAfterWindow_StartsANewRun()
    {
        var g = Gesture();
        g.Press(T0);
        g.Press(T0.AddSeconds(2)); // well beyond the window -> resets to a single press
        Assert.Equal(MediaButtonAction.PlayPause, g.Flush(T0.AddSeconds(3)));
    }

    [Fact]
    public void Flush_WithNoPresses_IsNull()
    {
        Assert.Null(Gesture().Flush(T0));
    }
}
