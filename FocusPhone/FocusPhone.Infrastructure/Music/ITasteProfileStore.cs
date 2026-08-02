using FocusPhone.Domain.Music;

namespace FocusPhone.Infrastructure.Music;

/// <summary>
/// Where the summarized Taste Profile lives. The production store keeps it as one JSON file in the owner's
/// Google Drive — the profile is the only part of Listening Genius that leaves the device — so a reinstall
/// or a second com.storyworthtelling app can read it. A load failure behaves as if none were saved.
/// </summary>
public interface ITasteProfileStore
{
    Task<TasteProfile> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(TasteProfile profile, CancellationToken cancellationToken);
}
