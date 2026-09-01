using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class PlaybackModesTests
{
    [Fact]
    public void Default_IsOffAndNoShuffle()
    {
        Assert.Equal(RepeatMode.Off, PlaybackModes.Default.Repeat);
        Assert.False(PlaybackModes.Default.Shuffle);
    }

    [Fact]
    public void CycleRepeat_StepsOffAllOneOff()
    {
        var m = PlaybackModes.Default;
        Assert.Equal(RepeatMode.All, (m = m.CycleRepeat()).Repeat);
        Assert.Equal(RepeatMode.One, (m = m.CycleRepeat()).Repeat);
        Assert.Equal(RepeatMode.Off, m.CycleRepeat().Repeat);
    }

    [Fact]
    public void ToggleShuffle_Flips()
    {
        Assert.True(PlaybackModes.Default.ToggleShuffle().Shuffle);
        Assert.False(PlaybackModes.Default.ToggleShuffle().ToggleShuffle().Shuffle);
    }

    [Fact]
    public void CycleRepeat_LeavesShuffleUntouched()
    {
        var shuffled = PlaybackModes.Default.ToggleShuffle();
        Assert.True(shuffled.CycleRepeat().Shuffle);
    }
}
