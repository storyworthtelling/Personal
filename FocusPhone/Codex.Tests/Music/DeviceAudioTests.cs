using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

/// <summary>
/// Per-output-device volume and equalizer preset (audit W7.3, owner's ruling to build it): the car comes back
/// loud and the headphones quiet without fiddling. The route decides which device is "this one"; the memory
/// restores on arrival and learns what the owner sets while it is there.
/// </summary>
public class DeviceAudioTests
{
    [Fact]
    public void BluetoothOutranksAWiredHeadsetWhichOutranksTheSpeaker()
    {
        Assert.Equal("bluetooth:Example Car Kit", DeviceAudioRoute.KeyFor([
            new AudioOutput(AudioOutputKind.Speaker, "Phone"),
            new AudioOutput(AudioOutputKind.Wired, "Headset"),
            new AudioOutput(AudioOutputKind.Bluetooth, "Example Car Kit")]));
        Assert.Equal("wired:Headset", DeviceAudioRoute.KeyFor([
            new AudioOutput(AudioOutputKind.Speaker, "Phone"),
            new AudioOutput(AudioOutputKind.Wired, "Headset")]));
        Assert.Equal("speaker", DeviceAudioRoute.KeyFor([new AudioOutput(AudioOutputKind.Speaker, "Phone")]));
    }

    [Fact]
    public void NothingPlayableIsNoDevice()
    {
        Assert.Null(DeviceAudioRoute.KeyFor([new AudioOutput(AudioOutputKind.Other, "Earpiece")]));
        Assert.Null(DeviceAudioRoute.KeyFor([]));
    }

    [Fact]
    public void ADeviceSeenForTheFirstTimeRestoresNothing()
    {
        var memory = new DeviceAudioMemory(new PlaybackDeviceSettings());

        Assert.Null(memory.Routed("bluetooth:Example Car Kit"));
    }

    [Fact]
    public void WhatTheOwnerSetsOnADeviceComesBackWhenItReturns()
    {
        var memory = new DeviceAudioMemory(new PlaybackDeviceSettings());
        memory.Routed("bluetooth:Example Car Kit");
        Assert.True(memory.Observe(80, "Road"));

        memory.Routed("wired:Headset");
        memory.Observe(30, null);

        var back = memory.Routed("bluetooth:Example Car Kit");
        Assert.Equal(80, back!.VolumePercent);
        Assert.Equal("Road", back.EqualizerPreset);
    }

    [Fact]
    public void AnUnchangedObservationWritesNothing()
    {
        var memory = new DeviceAudioMemory(new PlaybackDeviceSettings());
        memory.Routed("speaker");
        memory.Observe(50, null);

        Assert.False(memory.Observe(50, null));
    }

    [Fact]
    public void NothingIsLearnedBeforeARouteIsKnown()
    {
        var settings = new PlaybackDeviceSettings();
        var memory = new DeviceAudioMemory(settings);

        Assert.False(memory.Observe(70, "Road"));
        Assert.Empty(settings.Export());
    }
}

/// <summary>Sound check (W7.3): ReplayGain evens loudness between tracks, a boost through the enhancer and a cut through the player's volume.</summary>
public class SoundCheckTests
{
    [Fact]
    public void OffLeavesOnlyTheManualBoost()
    {
        var plan = SoundCheck.Plan(replayGainDb: -6, enabled: false, manualBoostMillibels: 300);

        Assert.Equal(300, plan.BoostMillibels);
        Assert.Equal(1f, plan.Volume);
    }

    [Fact]
    public void AQuietTrackIsBoosted()
    {
        var plan = SoundCheck.Plan(replayGainDb: 4, enabled: true, manualBoostMillibels: 0);

        Assert.Equal(400, plan.BoostMillibels);
        Assert.Equal(1f, plan.Volume);
    }

    [Fact]
    public void ALoudTrackIsCutThroughThePlayerVolumeNotTheEnhancer()
    {
        var plan = SoundCheck.Plan(replayGainDb: -6, enabled: true, manualBoostMillibels: 200);

        Assert.Equal(200, plan.BoostMillibels);
        Assert.Equal(0.501f, plan.Volume, 3);
    }

    [Fact]
    public void ATrackWithNoGainTagPlaysAsItIs()
    {
        var plan = SoundCheck.Plan(replayGainDb: null, enabled: true, manualBoostMillibels: 0);

        Assert.Equal(0, plan.BoostMillibels);
        Assert.Equal(1f, plan.Volume);
    }

    [Fact]
    public void TheAdjustmentIsClamped()
    {
        Assert.Equal(SoundCheck.MaxBoostMillibels, SoundCheck.Plan(30, true, 0).BoostMillibels);
        Assert.True(SoundCheck.Plan(-40, true, 0).Volume >= SoundCheck.QuietestVolume);
    }
}
