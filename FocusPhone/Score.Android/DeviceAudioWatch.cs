using Android.Content;
using Android.Media;
using Android.OS;
using Codex.Domain.Music;
using Codex.Ui;

namespace Score;

/// <summary>
/// Per-output-device volume and equalizer preset (audit W7.3): when the route moves to a car, a pair of
/// headphones or the speaker, what that device was last set to comes back, and what the owner sets while it
/// plays is learned for it. The decisions are <see cref="DeviceAudioRoute"/> and <see cref="DeviceAudioMemory"/>;
/// this only reads Android's outputs, sets the music stream's volume and applies the remembered preset. A
/// device's name never reaches the log — it can be a person's name.
/// </summary>
internal sealed class DeviceAudioWatch : AudioDeviceCallback
{
    private readonly AudioManager audio;
    private readonly EqualizerStore store;
    private readonly Action reapply;
    private readonly PlaybackDeviceSettings devices;
    private readonly DeviceAudioMemory memory;

    public DeviceAudioWatch(Context context, EqualizerStore store, Action reapply)
    {
        audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
        this.store = store;
        this.reapply = reapply;
        devices = store.LoadDevices();
        memory = new DeviceAudioMemory(devices);
    }

    public void Start(Handler handler)
    {
        audio.RegisterAudioDeviceCallback(this, handler);
        Route();
    }

    public void Stop() => audio.UnregisterAudioDeviceCallback(this);

    public override void OnAudioDevicesAdded(AudioDeviceInfo[]? addedDevices) => Route();

    public override void OnAudioDevicesRemoved(AudioDeviceInfo[]? removedDevices) => Route();

    /// <summary>What the current device is set to now; saved when it changed. Called on the service's tick.</summary>
    public void Observe()
    {
        var max = audio.GetStreamMaxVolume(Android.Media.Stream.Music);
        if (max <= 0)
        {
            return;
        }
        var percent = (int)Math.Round(audio.GetStreamVolume(Android.Media.Stream.Music) * 100.0 / max);
        if (memory.Observe(percent, store.LoadLibrary()?.InForce))
        {
            store.SaveDevices(devices);
        }
    }

    private void Route()
    {
        var outputs = audio.GetDevices(GetDevicesTargets.Outputs) ?? [];
        var key = DeviceAudioRoute.KeyFor(outputs.Select(Describe));
        if (key is null || key == memory.Current)
        {
            return;
        }

        var restore = memory.Routed(key);
        if (restore is null)
        {
            Observe(); // a device seen for the first time starts from where things are now
            return;
        }

        var max = audio.GetStreamMaxVolume(Android.Media.Stream.Music);
        audio.SetStreamVolume(Android.Media.Stream.Music, (int)Math.Round(restore.VolumePercent * max / 100.0), 0);
        if (restore.EqualizerPreset is { } preset && store.LoadLibrary() is { } library)
        {
            store.SaveLibrary(library.ApplyPreset(preset));
        }
        reapply();
        HeadLog.Info("DeviceAudio", $"Restored the settings for a {key.Split(':')[0]} output.");
    }

    private static AudioOutput Describe(AudioDeviceInfo device)
    {
        var kind = device.Type switch
        {
            AudioDeviceType.BluetoothA2dp => AudioOutputKind.Bluetooth,
            AudioDeviceType.WiredHeadset or AudioDeviceType.WiredHeadphones => AudioOutputKind.Wired,
            AudioDeviceType.UsbHeadset or AudioDeviceType.UsbDevice => AudioOutputKind.Usb,
            AudioDeviceType.BuiltinSpeaker => AudioOutputKind.Speaker,
            _ => AudioOutputKind.Other,
        };
        return new AudioOutput(kind, device.ProductName?.ToString() ?? string.Empty);
    }
}
