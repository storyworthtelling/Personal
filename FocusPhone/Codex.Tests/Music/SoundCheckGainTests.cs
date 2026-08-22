using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class SoundCheckGainTests
{
    [Fact]
    public void QuieterTrack_IsBoosted()
    {
        // Track at -20 LUFS, reference -14 -> +6 dB -> +600 mB.
        Assert.Equal(600, SoundCheckGain.Millibels(-20, -14, maxBoostMillibels: 1500, maxCutMillibels: 1500));
    }

    [Fact]
    public void LouderTrack_IsCut()
    {
        // Track at -8 LUFS, reference -14 -> -6 dB -> -600 mB.
        Assert.Equal(-600, SoundCheckGain.Millibels(-8, -14, 1500, 1500));
    }

    [Fact]
    public void Boost_IsClamped()
    {
        Assert.Equal(300, SoundCheckGain.Millibels(-40, -14, maxBoostMillibels: 300, maxCutMillibels: 1500));
    }

    [Fact]
    public void Cut_IsClamped()
    {
        Assert.Equal(-300, SoundCheckGain.Millibels(0, -14, maxBoostMillibels: 1500, maxCutMillibels: 300));
    }
}
