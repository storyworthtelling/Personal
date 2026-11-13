using Foreword.Domain.Interfaces;
using Foreword.Domain.Prayer;
using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Health;

namespace Foreword.Infrastructure.Prayer;

/// <summary>How a save of the prayer lists went (audit 11 §25).</summary>
public enum PrayerSaveOutcome
{
    Saved,
    WrongPassphrase,
    Unreadable,
    Refused,
    Offline,
    Failed,
}

/// <summary>
/// Saves the rotation list into the archive, leaving everything else as it was found, and says which way it went. Every
/// failure used to come back as <c>false</c>, so the screen could not tell no network from an archive that refused to
/// be written over. Never throws: the caller is a button.
/// </summary>
/// <param name="Cause">For <see cref="PrayerSaveOutcome.Failed"/>, the cause in plain words; never anything written on the page.</param>
public sealed record PrayerListSave(PrayerSaveOutcome Outcome, PrayerArchiveStatus Status, string Cause)
{
    public static async Task<PrayerListSave> Run(IPrayerArchive archive, string passphrase, IReadOnlyList<PrayerRotation> rotations)
    {
        PrayerArchiveRead opened;
        try
        {
            opened = await archive.LoadAsync(passphrase, CancellationToken.None);
        }
        catch (Exception failure)
        {
            return FromFailure(failure, PrayerArchiveStatus.Empty);
        }

        if (opened.Status == PrayerArchiveStatus.WrongPassphrase)
        {
            return new PrayerListSave(PrayerSaveOutcome.WrongPassphrase, opened.Status, string.Empty);
        }
        if (opened.Status == PrayerArchiveStatus.Corrupt)
        {
            return new PrayerListSave(PrayerSaveOutcome.Unreadable, opened.Status, string.Empty);
        }

        try
        {
            await archive.SaveAsync(opened.Archive with { Rotations = rotations }, passphrase, CancellationToken.None);
        }
        catch (Exception failure)
        {
            return FromFailure(failure, opened.Status);
        }

        return new PrayerListSave(PrayerSaveOutcome.Saved, opened.Status, string.Empty);
    }

    public string Sentence() => Outcome switch
    {
        PrayerSaveOutcome.Saved => string.Empty,
        PrayerSaveOutcome.WrongPassphrase => "The archive would not open with the passphrase in Settings. Nothing was changed.",
        PrayerSaveOutcome.Unreadable => "The archive in Drive could not be read. Nothing was changed — it is the only copy.",
        PrayerSaveOutcome.Refused => "The archive refused to be written over, because what is in Drive would not open. Nothing was changed.",
        PrayerSaveOutcome.Offline => "The phone has no network, so the archive could not be reached. Nothing was changed; try again once it is back.",
        _ => $"The lists could not be saved: {Cause}. Nothing was changed.",
    };

    // The archive's own refusal is not a bug to route around; no network is not a failure of the archive. Only the
    // exception's shape travels — its message could, in principle, carry what the archive holds.
    private static PrayerListSave FromFailure(Exception failure, PrayerArchiveStatus status) => failure switch
    {
        PrayerArchiveLockedException => new PrayerListSave(PrayerSaveOutcome.Refused, status, string.Empty),
        _ when NetworkFailure.IsOffline(failure) => new PrayerListSave(PrayerSaveOutcome.Offline, status, string.Empty),
        _ => new PrayerListSave(PrayerSaveOutcome.Failed, status, PlainError.Describe(failure, "Google Drive")),
    };
}
