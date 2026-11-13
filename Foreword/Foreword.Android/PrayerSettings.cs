using Foreword.Domain.Interfaces;
using Foreword.Domain.Prayer;

namespace Foreword.Android;

/// <summary>
/// What the rotations screen edits, read and written through the encrypted archive.
///
/// <para><b>Why not Preferences.</b> Every other screen's settings live on the phone, but the
/// rotations are the one piece of prayer configuration that has to survive the factory reset the
/// cutover performs — and the archive is the thing that already does. Keeping them here means one
/// source of truth for what the morning prints, and a new phone restores the lists along with
/// everything written on them.</para>
///
/// <para>Every save is a read-modify-write, so the morning's own save and an edit made while it runs
/// cannot silently discard each other's work. The window is not zero — one owner and one nightly
/// worker make it small enough to accept, and stated rather than hidden.</para>
/// </summary>
public sealed class PrayerSettings(IPrayerArchive archive)
{
    public PrayerArchiveStatus Status { get; private set; } = PrayerArchiveStatus.Empty;

    public IReadOnlyList<PrayerRotation> Rotations { get; private set; } = [];

    public async Task Load()
    {
        var opened = await archive.LoadAsync(CredentialStore.VaultPassphrase, CancellationToken.None);

        Status = opened.Status;
        Rotations = opened.Archive.Rotations;
    }

    /// <summary>
    /// Replaces the rotation list, leaving everything else in the archive exactly as it was found, and says which
    /// way it went (audit 11 §25) — the screen says so rather than appearing to have saved.
    /// </summary>
    public async Task<Foreword.Infrastructure.Prayer.PrayerListSave> Save(IReadOnlyList<PrayerRotation> rotations)
    {
        var result = await Foreword.Infrastructure.Prayer.PrayerListSave.Run(archive, CredentialStore.VaultPassphrase, rotations);
        Status = result.Status;
        if (result.Outcome == Foreword.Infrastructure.Prayer.PrayerSaveOutcome.Saved)
        {
            Rotations = rotations;
        }
        return result;
    }

    /// <summary>
    /// What the screen says while it has nothing to show, and after a save that did not land. A
    /// status sentence, not a spinner (§6).
    /// </summary>
    public string Sentence() => Status switch
    {
        PrayerArchiveStatus.WrongPassphrase =>
            "The archive would not open with the passphrase in Settings. Nothing was changed.",
        PrayerArchiveStatus.Corrupt =>
            "The archive in Drive could not be read. Nothing was changed — it is the only copy.",
        PrayerArchiveStatus.Empty when string.IsNullOrEmpty(CredentialStore.VaultPassphrase) =>
            "Enter your vault passphrase in Settings, and the lists you keep here will be saved with it.",
        PrayerArchiveStatus.Empty => "Nothing kept yet.",
        _ => Rotations.Count == 0
            ? "No lists yet. A list is a set of names you pray through one a day."
            : "One name from each list prints every morning, in order.",
    };
}
