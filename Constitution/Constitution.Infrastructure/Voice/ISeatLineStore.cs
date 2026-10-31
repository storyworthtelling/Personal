using Constitution.Domain.Voice;

namespace Constitution.Infrastructure.Voice;

/// <summary>
/// Where a day's voiced lines are kept, so a re-render, a tab switch or a relaunch asks no one
/// anything. The seam is synchronous because the real store is the device's preferences (an Android
/// concern, later); tests use an in-memory one. Only voiced lines are ever saved.
/// </summary>
public interface ISeatLineStore
{
    /// <summary>The saved line for this key, or null.</summary>
    SeatLine? Find(SeatLineKey key);

    void Save(SeatLineKey key, SeatLine line);

    /// <summary>Forget every line filed under a day earlier than this one.</summary>
    void DropBefore(DateOnly day);
}
