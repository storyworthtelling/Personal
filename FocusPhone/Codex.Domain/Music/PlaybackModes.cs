namespace Codex.Domain.Music;

/// <summary>Repeat behavior for the now-playing queue.</summary>
public enum RepeatMode
{
    Off,
    All,
    One,
}

/// <summary>
/// The now-playing shuffle + repeat state, with the iPod-style cycle (repeat button steps
/// Off → All → One → Off). Pure; the screen toggles it and pushes the result to the player.
/// </summary>
public sealed record PlaybackModes(RepeatMode Repeat, bool Shuffle)
{
    public static PlaybackModes Default { get; } = new(RepeatMode.Off, false);

    public PlaybackModes CycleRepeat() => this with
    {
        Repeat = Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        }
    };

    public PlaybackModes ToggleShuffle() => this with { Shuffle = !Shuffle };
}
