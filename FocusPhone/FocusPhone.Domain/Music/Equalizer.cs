namespace FocusPhone.Domain.Music;

/// <summary>
/// A custom equalizer curve: on/off plus a per-band gain in millibels (the unit Android's Equalizer uses),
/// one entry per band the device reports. Pure — the Android side reads the band count and min/max and
/// attaches the effect; this owns the values, the clamp, and per-band edits.
/// </summary>
public sealed record EqualizerSettings(bool Enabled, IReadOnlyList<short> BandGains)
{
    public static EqualizerSettings Flat(int bandCount) =>
        new(false, [.. Enumerable.Repeat((short)0, Math.Max(0, bandCount))]);

    public EqualizerSettings WithBand(int index, short gain)
    {
        if (index < 0 || index >= BandGains.Count)
        {
            return this;
        }

        var gains = BandGains.ToArray();
        gains[index] = gain;
        return this with { BandGains = gains };
    }

    /// <summary>Clamps every band into the device's supported range (min/max millibels).</summary>
    public EqualizerSettings Clamp(short minMillibels, short maxMillibels) =>
        this with { BandGains = [.. BandGains.Select(g => (short)Math.Clamp(g, minMillibels, maxMillibels))] };
}

/// <summary>A user-saved equalizer curve, referenced by name.</summary>
public sealed record EqualizerPreset(string Name, IReadOnlyList<short> BandGains);

/// <summary>
/// The active curve plus the owner's saved presets. Saving snapshots the active band gains under a name
/// (replacing one of the same name); applying loads a preset's gains into the active curve, enabling it.
/// </summary>
public sealed record EqualizerLibrary(EqualizerSettings Active, IReadOnlyList<EqualizerPreset> Presets)
{
    public static EqualizerLibrary Flat(int bandCount) => new(EqualizerSettings.Flat(bandCount), []);

    public EqualizerLibrary SavePreset(string name)
    {
        var others = Presets.Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        return this with { Presets = [.. others, new EqualizerPreset(name, Active.BandGains)] };
    }

    public EqualizerLibrary ApplyPreset(string name)
    {
        var preset = Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        return preset is null ? this : this with { Active = new EqualizerSettings(true, preset.BandGains) };
    }
}
